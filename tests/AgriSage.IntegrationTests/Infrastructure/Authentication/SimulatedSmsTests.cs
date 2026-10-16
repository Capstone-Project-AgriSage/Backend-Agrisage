using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Messaging;
using AgriSage.IntegrationTests.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

// The simulated SMS provider (MessageDelivery:SmsProvider = Simulated): no SMS leaves the machine, the code goes to an
// in-memory outbox that a development-only page shows. Offline.
public sealed class SimulatedSmsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_simulated_sms_makes_no_http_call_and_lands_in_the_outbox_with_its_code()
    {
        var (sender, handler, outbox) = Build(new MessageDeliveryOptions { SmsProvider = "Simulated" });

        await sender.SendAsync(Otp("0901234567", "654321"), Token);

        Assert.Equal(0, handler.Calls);
        var sent = Assert.Single(outbox.Recent());
        Assert.Equal("0901234567", sent.Destination);
        Assert.Equal("654321", sent.Code);
        Assert.Equal("PasswordReset", sent.Purpose);
        Assert.Contains("654321", sent.Text);
        Assert.True(sent.Text.All(c => c < 128));
    }

    [Fact]
    public void The_simulated_provider_needs_no_gateway_address_or_key_and_email_is_not_affected()
    {
        var (sender, _, _) = Build(new MessageDeliveryOptions { SmsProvider = "Simulated" });

        Assert.True(sender.IsConfigured(AuthDeliveryChannel.Sms));
        Assert.False(sender.IsConfigured(AuthDeliveryChannel.Email));
    }

    [Fact]
    public async Task The_cost_guards_do_not_apply_because_nothing_is_sent()
    {
        var (sender, handler, outbox) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "Simulated", SmsAllowedRecipients = ["0901234567"], SmsDailyLimit = 1,
        });

        await sender.SendAsync(Otp("0901234567", "111111"), Token);
        await sender.SendAsync(Otp("0933333333", "222222"), Token); // not on the allow-list
        await sender.SendAsync(Otp("0901234567", "333333"), Token); // over the daily limit

        Assert.Equal(0, handler.Calls);
        Assert.Equal(["333333", "222222", "111111"], outbox.Recent().Select(m => m.Code));
    }

    [Fact]
    public void The_outbox_keeps_the_newest_fifty_first()
    {
        var outbox = new SimulatedSmsOutbox();
        for (var i = 0; i < SimulatedSmsOutbox.Capacity + 5; i++)
            outbox.Add(new SimulatedSmsMessage(Noon, "0901234567", "PasswordReset", i.ToString("D6"), "x"));

        var recent = outbox.Recent();
        Assert.Equal(SimulatedSmsOutbox.Capacity, recent.Count);
        Assert.Equal("000054", recent[0].Code);
        Assert.Equal("000005", recent[^1].Code);
    }

    [Theory]
    [InlineData("Simulated", true)]
    [InlineData(" simulated ", true)]
    [InlineData("AndroidGateway", false)]
    [InlineData("Generic", false)]
    [InlineData("", false)]
    public void The_mode_is_read_from_the_configuration(string provider, bool simulated)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MessageDelivery:SmsProvider"] = provider }).Build();

        Assert.Equal(simulated, SimulatedSmsMode.IsSimulated(configuration));
    }

    [Fact]
    public void The_simulated_provider_is_refused_outside_development()
    {
        SimulatedSmsMode.EnsureAllowed(simulated: true, isDevelopment: true);
        SimulatedSmsMode.EnsureAllowed(simulated: false, isDevelopment: false);
        Assert.Throws<InvalidOperationException>(() => SimulatedSmsMode.EnsureAllowed(simulated: true, isDevelopment: false));
    }

    // ---- the development-only page ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_outbox_page_is_404_unless_the_api_runs_with_simulated_sms()
    {
        await using var real = new StaffHttpTests.StaffApiFactory();
        using var client = real.CreateClient();

        Assert.Null(real.Services.GetService<ISimulatedSmsOutbox>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/dev/sms-outbox", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/dev/sms-outbox/view", Token)).StatusCode);
    }

    [Fact]
    public async Task In_development_with_simulated_sms_a_sent_code_shows_in_the_outbox_without_signing_in()
    {
        await using var app = new StaffHttpTests.StaffApiFactory()
            .WithWebHostBuilder(b => b.UseSetting("MessageDelivery:SmsProvider", "Simulated"));
        var sender = app.Services.GetRequiredService<IAuthMessageSender>();
        Assert.True(sender.IsConfigured(AuthDeliveryChannel.Sms));
        await sender.SendAsync(Otp("0912345678", "246810"), Token);

        using var client = app.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/dev/sms-outbox", Token);
        Assert.Equal("246810", list[0].GetProperty("code").GetString());
        Assert.Equal("0912345678", list[0].GetProperty("destination").GetString());

        var view = await client.GetAsync("/api/dev/sms-outbox/view", Token);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal("text/html", view.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("Production", "Simulated", "Development")]
    [InlineData("Staging", "simulated", "Development")]
    [InlineData("Development", "Fake", "SmsProvider must be")]
    public void The_api_refuses_to_start_with_simulated_sms_outside_development_or_with_an_unknown_provider(
        string environment, string provider, string expectedInMessage)
    {
        using var app = new StaffHttpTests.StaffApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("MessageDelivery:SmsProvider", provider);
        });

        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(expectedInMessage, error.ToString());
    }

    private static (AuthMessageSender Sender, CountingHandler Handler, SimulatedSmsOutbox Outbox) Build(MessageDeliveryOptions options)
    {
        var handler = new CountingHandler();
        var outbox = new SimulatedSmsOutbox();
        var clock = new FixedClock(Noon);
        var sender = new AuthMessageSender(new HttpClient(handler), Options.Create(options), new SmsDailyBudget(clock), clock,
            outbox: outbox);
        return (sender, handler, outbox);
    }

    private static AuthMessage Otp(string destination, string code) => new(AuthDeliveryChannel.Sms, destination,
        AuthChallengePurpose.PasswordReset, code, Noon.AddMinutes(10));

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}
