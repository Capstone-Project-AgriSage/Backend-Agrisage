using System.Net;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

// Offline tests of the "SMS Gateway for Android" adapter (capcom6): request shape, which gateway addresses are
// accepted, and the two cost guards (allowed recipients, daily limit).
public sealed class AndroidSmsGatewayTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 14, 5, 0, 0, TimeSpan.Zero); // 12:00 in Vietnam

    [Fact]
    public async Task Android_gateway_gets_basic_auth_an_international_number_and_one_short_ascii_message()
    {
        var (sender, handler) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = "http://192.168.1.147:8080/message",
            SmsUsername = "sms", SmsApiKey = "test-only-password",
        });

        await sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.Equal("http://192.168.1.147:8080/message", handler.Uri);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("sms:test-only-password"));
        Assert.Equal($"Basic {expected}", handler.Authorization);
        var body = JsonSerializer.Deserialize<JsonElement>(handler.Body!);
        Assert.Equal("+84901234567", body.GetProperty("phoneNumbers")[0].GetString());
        Assert.Single(body.GetProperty("phoneNumbers").EnumerateArray());
        var text = body.GetProperty("textMessage").GetProperty("text").GetString()!;
        Assert.Contains("123456", text);
        Assert.Contains("10 phut", text);
        Assert.True(text.All(c => c < 128), "diacritics would turn the SMS into UCS-2 and cost more segments");
        Assert.True(text.Length <= 160, "a 6-digit OTP message must fit one GSM-7 segment");
    }

    [Theory]
    [InlineData("http://192.168.1.147:8080/message", true)]
    [InlineData("http://10.0.0.5:8080/message", true)]
    [InlineData("http://172.16.0.9:8080/message", true)]
    [InlineData("http://172.31.255.1:8080/message", true)]
    [InlineData("http://127.0.0.1:8080/message", true)]
    [InlineData("http://localhost:8080/message", true)]
    [InlineData("https://api.sms-gate.app/3rdparty/v1/message", true)]
    [InlineData("http://172.32.0.1:8080/message", false)]
    [InlineData("http://8.8.8.8:8080/message", false)]
    [InlineData("http://example.test/message", false)]
    [InlineData("ftp://192.168.1.147/message", false)]
    public void Plain_http_is_accepted_only_for_private_network_addresses(string url, bool configured)
    {
        var (sender, _) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = url, SmsUsername = "sms", SmsApiKey = "test-only-password",
        });

        Assert.Equal(configured, sender.IsConfigured(AuthDeliveryChannel.Sms));
    }

    [Fact]
    public void Android_gateway_needs_a_username_and_the_generic_provider_still_requires_https()
    {
        var (noUser, _) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = "http://192.168.1.147:8080/message", SmsApiKey = "test-only-password",
        });
        Assert.False(noUser.IsConfigured(AuthDeliveryChannel.Sms));

        var (generic, _) = Build(new MessageDeliveryOptions
        {
            SmsGatewayUrl = "http://192.168.1.147:8080/message", SmsApiKey = "test-only-key",
        });
        Assert.False(generic.IsConfigured(AuthDeliveryChannel.Sms));
    }

    [Fact]
    public async Task Only_allowed_recipients_get_a_real_sms_and_a_refused_number_never_reaches_the_gateway()
    {
        var (sender, handler) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = "http://192.168.1.147:8080/message",
            SmsUsername = "sms", SmsApiKey = "test-only-password",
            SmsAllowedRecipients = ["+84901234567", "0912 345 678"],
        });

        await sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken);
        await sender.SendAsync(Otp("0912345678"), TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Calls);

        await Assert.ThrowsAsync<MessageDeliveryUnavailableException>(
            () => sender.SendAsync(Otp("0933333333"), TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Daily_limit_stops_real_sms_and_starts_again_on_the_next_vietnam_day()
    {
        var clock = new FakeClock(Noon);
        var (sender, handler) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = "http://192.168.1.147:8080/message",
            SmsUsername = "sms", SmsApiKey = "test-only-password", SmsDailyLimit = 2,
        }, clock);

        await sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken);
        await sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<MessageDeliveryUnavailableException>(
            () => sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Calls);

        clock.UtcNow = new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.Zero); // 00:00 on 15 Oct in Vietnam
        await sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public void Daily_budget_rolls_over_at_midnight_in_vietnam_and_zero_means_unlimited()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 14, 16, 59, 0, TimeSpan.Zero)); // 23:59 in Vietnam
        var budget = new SmsDailyBudget(clock);

        Assert.True(budget.TryTake(1));
        Assert.False(budget.TryTake(1));

        clock.UtcNow = new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.Zero);
        Assert.True(budget.TryTake(1));

        for (var i = 0; i < 50; i++) Assert.True(budget.TryTake(0));
    }

    [Fact]
    public async Task A_gateway_failure_is_reported_as_unavailable()
    {
        var (sender, handler) = Build(new MessageDeliveryOptions
        {
            SmsProvider = "AndroidGateway", SmsGatewayUrl = "http://192.168.1.147:8080/message",
            SmsUsername = "sms", SmsApiKey = "test-only-password",
        });
        handler.Status = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<MessageDeliveryUnavailableException>(
            () => sender.SendAsync(Otp("0901234567"), TestContext.Current.CancellationToken));
    }

    private static (AuthMessageSender Sender, CapturingHandler Handler) Build(
        MessageDeliveryOptions options, FakeClock? clock = null)
    {
        clock ??= new FakeClock(Noon);
        var handler = new CapturingHandler();
        var sender = new AuthMessageSender(new HttpClient(handler), Options.Create(options), new SmsDailyBudget(clock), clock);
        return (sender, handler);
    }

    private static AuthMessage Otp(string destination) => new(AuthDeliveryChannel.Sms, destination,
        AuthChallengePurpose.PhoneVerification, "123456", Noon.AddMinutes(10));

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Uri { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Accepted;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return new(Status);
        }
    }
}
