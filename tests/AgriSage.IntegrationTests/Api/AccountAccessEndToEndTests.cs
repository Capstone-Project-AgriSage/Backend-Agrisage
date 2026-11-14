using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// Account access end to end (secondary flows of the demo): a Farmer registers, confirms the phone with the SMS code, signs in,
// refreshes the session (the used refresh token is spent) and signs out; a Farmer who forgot the password gets an SMS code,
// checks it, sets a new password, and every earlier session ends. Every call goes through HTTP with the real services; only
// the SMS gateway is replaced by an outbox the test reads. Each test stays within the 10-a-minute auth rate limit.
public sealed class AccountAccessEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private const string Phone = "0987654321";
    private const string Password = "lua-mua-2026";

    [CreditDbFact]
    public async Task A_farmer_registers_confirms_the_phone_signs_in_refreshes_and_signs_out()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var sms = new SmsOutbox();
        await using var host = Start(factory, e, sms.Install);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var anonymous = host.App.CreateClient();

        // Register: signed in at once; the same phone cannot register twice.
        var registered = await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Nông dân mới", phoneNumber = Phone, email = (string?)null, password = Password }, token), token);
        Assert.Equal("FARMER", registered.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await anonymous.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Trùng số", phoneNumber = Phone, email = (string?)null, password = Password }, token)).StatusCode);
        using var farmer = Bearer(host, registered);

        // Confirm the phone with the 6-digit SMS code; a wrong code is refused.
        Assert.False((await OkAsync(await farmer.GetAsync("/api/auth/me", token), token)).GetProperty("phoneVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, (await farmer.PostAsync("/api/auth/phone-verification/request", null, token)).StatusCode);
        var code = sms.Last(AuthChallengePurpose.PhoneVerification).Token;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.False((await farmer.PostAsJsonAsync("/api/auth/phone-verification/confirm", new { code = Wrong(code) }, token)).IsSuccessStatusCode);
        await OkAsync(await farmer.PostAsJsonAsync("/api/auth/phone-verification/confirm", new { code }, token), token);
        Assert.True((await OkAsync(await farmer.GetAsync("/api/auth/me", token), token)).GetProperty("phoneVerified").GetBoolean());

        // Sign in by phone: a wrong password is refused; the refresh token rotates. Presenting the spent one again is
        // treated as theft (REFRESH_TOKEN_REUSE): it is refused and the whole session ends, the new token included.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new { identifier = Phone, password = "sai-mat-khau" }, token)).StatusCode);
        var login = await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/login", new { identifier = Phone, password = Password }, token), token);
        var refreshToken = login.GetProperty("refreshToken").GetString();
        var refreshed = await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, token), token);
        Assert.NotEqual(refreshToken, refreshed.GetProperty("refreshToken").GetString());
        using (var rotated = Bearer(host, refreshed)) await OkAsync(await rotated.GetAsync("/api/auth/me", token), token);
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken }, token)).IsSuccessStatusCode);
        using (var stolen = Bearer(host, refreshed))
            Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("/api/auth/me", token)).StatusCode);

        // Sign in again and sign out: that session's token no longer works.
        using var session = Bearer(host, await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/login",
            new { identifier = Phone, password = Password }, token), token));
        await OkAsync(await session.GetAsync("/api/auth/me", token), token);
        Assert.Equal(HttpStatusCode.NoContent, (await session.PostAsync("/api/auth/logout", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me", token)).StatusCode);
    }

    [CreditDbFact]
    public async Task A_forgotten_password_is_reset_with_the_sms_code_and_every_earlier_session_ends()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var sms = new SmsOutbox();
        await using var host = Start(factory, e, sms.Install);
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var anonymous = host.App.CreateClient();
        var registered = await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/register",
            new { fullName = "Nông dân quên mật khẩu", phoneNumber = Phone, email = (string?)null, password = Password }, token), token);
        using var oldSession = Bearer(host, registered);
        await OkAsync(await oldSession.GetAsync("/api/auth/me", token), token);

        // An unknown phone gets the same answer and no SMS (the API does not reveal who has an account).
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = "0911111111" }, token)).StatusCode);
        Assert.Empty(sms.Sent);

        // The real phone gets a code; a wrong code is refused; the right one is checked, then used once.
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = Phone }, token)).StatusCode);
        var code = sms.Last(AuthChallengePurpose.PasswordReset).Token;
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/reset-password/verify-code", new { identifier = Phone, code = Wrong(code) }, token)).IsSuccessStatusCode);
        await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/reset-password/verify-code", new { identifier = Phone, code }, token), token);
        const string newPassword = "mat-khau-moi-2026";
        await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/reset-password",
            new { token = (string?)null, newPassword, identifier = Phone, code }, token), token);

        // Every earlier session ends; the old password no longer works, the new one does; the old refresh token is dead.
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login",
            new { identifier = Phone, password = Password }, token)).StatusCode);
        await OkAsync(await anonymous.PostAsJsonAsync("/api/auth/login", new { identifier = Phone, password = newPassword }, token), token);
        Assert.False((await anonymous.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = registered.GetProperty("refreshToken").GetString() }, token)).IsSuccessStatusCode);
    }

    private static HttpClient Bearer(EndToEndHost host, JsonElement auth)
    {
        var client = host.App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    // Stands in for the SMS gateway: keeps what would have been sent.
    private sealed class SmsOutbox : IAuthMessageSender
    {
        public ConcurrentQueue<AuthMessage> Sent { get; } = new();

        public bool IsConfigured(AuthDeliveryChannel channel) => true;

        public Task SendAsync(AuthMessage message, CancellationToken cancellationToken)
        {
            Sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public AuthMessage Last(AuthChallengePurpose purpose) =>
            Sent.Last(m => m.Purpose == purpose && m.Channel == AuthDeliveryChannel.Sms);

        public void Install(IServiceCollection services)
        {
            services.RemoveAll<IAuthMessageSender>();
            services.AddSingleton<IAuthMessageSender>(this);
        }
    }
}
