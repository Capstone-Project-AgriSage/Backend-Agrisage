using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

public sealed class SecretTokenAndMessageTests
{
    [Fact]
    public void Secrets_are_random_and_hashes_are_bound_to_the_challenge()
    {
        var service = new SecretTokenService(Options.Create(OperationsTestEnvironment.Jwt));
        var first = service.GenerateToken(); var second = service.GenerateToken(); var id = Guid.NewGuid();
        Assert.Equal(43, first.Length); Assert.NotEqual(first, second);
        Assert.Equal(64, service.HashToken(first).Length); Assert.NotEqual(first, service.HashToken(first));
        var otp = service.GenerateOtp(); Assert.Matches("^[0-9]{6}$", otp);
        var hash = service.HashChallenge(id, otp);
        Assert.True(service.VerifyChallenge(id, otp, hash));
        Assert.False(service.VerifyChallenge(Guid.NewGuid(), otp, hash));
        Assert.False(service.VerifyChallenge(id, "bad", hash));
    }
    [Fact]
    public void Missing_transport_and_non_https_sms_gateway_are_not_configured()
    {
        using var http = new HttpClient();
        var missing = new AuthMessageSender(http, Options.Create(new MessageDeliveryOptions()));
        Assert.False(missing.IsConfigured(AuthDeliveryChannel.Email)); Assert.False(missing.IsConfigured(AuthDeliveryChannel.Sms));
        var insecure = new AuthMessageSender(http, Options.Create(new MessageDeliveryOptions
        { SmsGatewayUrl = "http://example.test/sms", SmsApiKey = "test-key" }));
        Assert.False(insecure.IsConfigured(AuthDeliveryChannel.Sms));
    }
    [Fact]
    public async Task Sms_adapter_sends_server_owned_destination_and_authentication_and_maps_failure()
    {
        var handler = new CapturingHandler(); using var http = new HttpClient(handler);
        var sender = new AuthMessageSender(http, Options.Create(new MessageDeliveryOptions
        { SmsGatewayUrl = "https://example.test/sms", SmsApiKey = "test-only-key" }));
        var message = new AuthMessage(AuthDeliveryChannel.Sms, "0901234567", AuthChallengePurpose.PhoneVerification, "123456", DateTimeOffset.UtcNow.AddMinutes(10));
        await sender.SendAsync(message, TestContext.Current.CancellationToken);
        Assert.Equal("Bearer test-only-key", handler.Authorization);
        var body = JsonSerializer.Deserialize<JsonElement>(handler.Body!);
        Assert.Equal(message.Destination, body.GetProperty("to").GetString());
        Assert.Contains(message.Token, body.GetProperty("message").GetString());
        handler.Status = HttpStatusCode.BadGateway;
        await Assert.ThrowsAsync<MessageDeliveryUnavailableException>(() => sender.SendAsync(message, TestContext.Current.CancellationToken));
    }
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; set; }
        public string? Authorization { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return new(Status);
        }
    }
}
