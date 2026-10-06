using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Payments;

// The in-memory gateway of the test environment (PayOS:Mode = Simulated) and the rules that keep it out of production.
// No database and no network.
public class SimulatedPaymentGatewayTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SimulatedPaymentGateway Gateway(string? returnUrl = "http://localhost:5174/payments/payos/return") => new(
        Options.Create(new PayOsOptions { ReturnUrl = returnUrl, LinkExpiryMinutes = 20 }),
        TimeProvider.System,
        NullLogger<SimulatedPaymentGateway>.Instance);

    private static IConfiguration Config(string? mode) => new ConfigurationBuilder()
        .AddInMemoryCollection(mode is null ? [] : new Dictionary<string, string?> { ["PayOS:Mode"] = mode })
        .Build();

    [Fact]
    public async Task A_link_is_pending_points_back_to_the_result_page_and_is_recognisable()
    {
        var gateway = Gateway();

        var link = await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(1791203105162645, 110_000, "PM202610060001"), Token);

        Assert.Equal("sim-1791203105162645", link.PaymentLinkId);
        Assert.Equal("http://localhost:5174/payments/payos/return?simulated=1&orderCode=1791203105162645", link.CheckoutUrl);
        Assert.Equal("SIMULATED", link.QrCode);
        Assert.True(link.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(19));
        var state = await gateway.GetPaymentLinkAsync(1791203105162645, Token);
        Assert.Equal((PaymentLinkStatus.Pending, 110_000L, 0L), (state.Status, state.Amount, state.AmountPaid));
    }

    [Theory]
    [InlineData("https://shop.example/paid?lang=vi", "https://shop.example/paid?lang=vi&simulated=1&orderCode=5")]
    [InlineData(null, "/payments/payos/return?simulated=1&orderCode=5")]
    [InlineData(" ", "/payments/payos/return?simulated=1&orderCode=5")]
    public async Task The_return_url_is_extended_with_the_right_separator_or_defaults_to_a_relative_path(string? returnUrl, string expected)
    {
        var link = await Gateway(returnUrl).CreatePaymentLinkAsync(new CreatePaymentLinkRequest(5, 1_000, "x"), Token);

        Assert.Equal(expected, link.CheckoutUrl);
    }

    [Fact]
    public async Task Marking_a_pending_link_paid_pays_it_in_full_once()
    {
        var gateway = Gateway();
        await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(7, 50_000, "x"), Token);

        Assert.True(gateway.TryMarkPaid(7));

        var state = await gateway.GetPaymentLinkAsync(7, Token);
        Assert.Equal((PaymentLinkStatus.Paid, 50_000L, 50_000L), (state.Status, state.Amount, state.AmountPaid));
        Assert.False(gateway.TryMarkPaid(7));
    }

    [Fact]
    public async Task An_unknown_link_cannot_be_paid_and_reads_as_expired_like_after_a_restart()
    {
        var gateway = Gateway();

        Assert.False(gateway.TryMarkPaid(99));

        var state = await gateway.GetPaymentLinkAsync(99, Token);
        Assert.Equal((PaymentLinkStatus.Expired, 0L), (state.Status, state.AmountPaid));
    }

    [Fact]
    public async Task A_cancelled_link_cannot_be_paid_and_a_paid_one_cannot_be_cancelled()
    {
        var gateway = Gateway();
        await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(1, 10_000, "x"), Token);
        await gateway.CreatePaymentLinkAsync(new CreatePaymentLinkRequest(2, 20_000, "x"), Token);

        var cancelled = await gateway.CancelPaymentLinkAsync(1, "Cancelled by the customer.", Token);
        Assert.Equal(PaymentLinkStatus.Cancelled, cancelled.Status);
        Assert.False(gateway.TryMarkPaid(1));

        Assert.True(gateway.TryMarkPaid(2));
        var stillPaid = await gateway.CancelPaymentLinkAsync(2, "late", Token);
        Assert.Equal((PaymentLinkStatus.Paid, 20_000L), (stillPaid.Status, stillPaid.AmountPaid));
    }

    [Fact]
    public async Task There_is_no_webhook_in_this_mode()
    {
        var gateway = Gateway();

        Assert.Equal(PaymentWebhookKind.NoData, (await gateway.VerifyWebhookAsync(" {} ", Token)).Kind);
        Assert.Equal(PaymentWebhookKind.Invalid, (await gateway.VerifyWebhookAsync("{\"data\":{},\"signature\":\"x\"}", Token)).Kind);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Real", false)]
    [InlineData(" real ", false)]
    [InlineData("Simulated", true)]
    [InlineData("simulated", true)]
    public void The_mode_defaults_to_real_and_ignores_case(string? mode, bool simulated) =>
        Assert.Equal(simulated, PayOsMode.IsSimulated(Config(mode)));

    [Theory]
    [InlineData("Fake")]
    [InlineData("Sim")]
    public void An_unknown_mode_is_a_startup_error_not_a_silent_fallback(string mode)
    {
        var error = Assert.Throws<InvalidOperationException>(() => PayOsMode.IsSimulated(Config(mode)));

        Assert.Contains("Real", error.Message);
        Assert.Contains("Simulated", error.Message);
    }

    [Fact]
    public void The_simulated_gateway_is_allowed_in_development_only()
    {
        PayOsMode.EnsureAllowed(simulated: true, isDevelopment: true);
        PayOsMode.EnsureAllowed(simulated: false, isDevelopment: true);
        PayOsMode.EnsureAllowed(simulated: false, isDevelopment: false);

        var error = Assert.Throws<InvalidOperationException>(() => PayOsMode.EnsureAllowed(simulated: true, isDevelopment: false));
        Assert.Contains("Development", error.Message);
    }
}
