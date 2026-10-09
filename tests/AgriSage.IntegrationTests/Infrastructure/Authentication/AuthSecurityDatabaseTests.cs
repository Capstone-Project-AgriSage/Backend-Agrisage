using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

public sealed class AuthSecurityDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [RealDbFact]
    public async Task Correct_phone_otp_verifies_only_the_current_contact()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.User.UpdateContact(e.User.Email, "09" + Random.Shared.Next(10_000_000, 99_999_999));
        await e.Context.SaveChangesAsync(Token);
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Sms, Token);
        await e.Challenges.ConfirmPhoneAsync(Assert.Single(e.Messages.Sent).Token, Token);
        Assert.True(e.User.PhoneVerified);
    }
    [RealDbFact]
    public async Task Sms_password_reset_uses_a_purpose_bound_high_entropy_token()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.User.UpdateContact(e.User.Email, "09" + Random.Shared.Next(10_000_000, 99_999_999));
        await e.Context.SaveChangesAsync(Token);
        await e.Challenges.RequestResetAsync(e.User.PhoneNumber!, Token);
        var message = Assert.Single(e.Messages.Sent);
        Assert.Equal(76, message.Token.Length);
        await e.Challenges.ResetPasswordAsync(message.Token, "sms-reset-password", Token);
        Assert.Equal(AgriSage.Application.Common.Interfaces.PasswordVerification.Success, e.Passwords.Verify(e.User.PasswordHash, "sms-reset-password"));
    }
    [RealDbFact]
    public async Task Anonymous_reset_acknowledges_unknown_and_delivery_failure_without_revealing_existence()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.Messages.Fail = true;
        await e.Challenges.RequestResetAsync(e.User.Email!, Token);
        await e.Challenges.RequestResetAsync($"{Guid.NewGuid():N}@example.test", Token);
        Assert.Empty(e.Messages.Sent);
        Assert.NotNull((await e.Context.AuthChallenges.SingleAsync(c => c.UserId == e.User.Id, Token)).ConsumedAt);
    }
    [RealDbFact]
    public async Task Refresh_rotates_hash_only_tokens_and_replay_revokes_the_successor_session()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var first = await e.StartAsync();
        var refreshed = await e.Sessions.RefreshAsync(first.RefreshToken!, Token);
        Assert.NotEqual(first.RefreshToken, refreshed.RefreshToken); Assert.Equal(first.SessionId, refreshed.SessionId);
        Assert.Equal(2, await e.Context.RefreshTokens.CountAsync(t => t.SessionId == first.SessionId, Token));
        Assert.DoesNotContain(await e.Context.RefreshTokens.Where(t => t.SessionId == first.SessionId).Select(t => t.TokenHash).ToListAsync(Token),
            h => h == first.RefreshToken || h == refreshed.RefreshToken);
        var jwt = new JsonWebToken(refreshed.AccessToken);
        Assert.Equal(first.SessionId.ToString(), jwt.GetClaim("sid").Value);
        Assert.True(await e.Validator.IsSessionAllowedAsync(e.User.Id, first.SessionId!.Value, e.User.SecurityVersion, "FARMER", Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Sessions.RefreshAsync(first.RefreshToken!, Token));
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, first.SessionId.Value, e.User.SecurityVersion, "FARMER", Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Sessions.RefreshAsync(refreshed.RefreshToken!, Token));
        Assert.Equal("REFRESH_TOKEN_REUSE", (await e.Context.AuthSessions.SingleAsync(s => s.Id == first.SessionId, Token)).RevocationReason);
    }
    [RealDbFact]
    public async Task Password_change_invalidates_access_refresh_and_old_sessions_immediately()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var first = await e.StartAsync(); var version = e.User.SecurityVersion;
        await e.Auth.ChangePasswordAsync(new ChangePasswordRequest("initial-password", "replacement-password"), Token);
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, first.SessionId!.Value, version, "FARMER", Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Sessions.RefreshAsync(first.RefreshToken!, Token));
        Assert.Empty(await e.Sessions.ListAsync(Token));
        var login = await e.Auth.LoginAsync(new LoginRequest(e.User.Email!, "replacement-password"), Token);
        Assert.True(await e.Validator.IsSessionAllowedAsync(e.User.Id, login.SessionId!.Value, e.User.SecurityVersion, "FARMER", Token));
    }
    [RealDbFact]
    public async Task Logout_revokes_only_own_session_and_other_users_session_is_not_found()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var first = await e.StartAsync(); var second = await e.StartAsync();
        await e.Sessions.RevokeAsync(first.SessionId!.Value, Token);
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, first.SessionId.Value, e.User.SecurityVersion, "FARMER", Token));
        Assert.True(await e.Validator.IsSessionAllowedAsync(e.User.Id, second.SessionId!.Value, e.User.SecurityVersion, "FARMER", Token));
        e.Current.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => e.Sessions.RevokeAsync(second.SessionId.Value, Token));
        e.Current.UserId = e.User.Id;
        await e.Sessions.LogoutAllAsync(Token);
        Assert.Empty(await e.Sessions.ListAsync(Token));
    }
    [RealDbFact]
    public async Task Expired_and_locked_then_unlocked_sessions_never_refresh()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var first = await e.StartAsync();
        e.User.ChangeStatus(UserStatus.Locked); e.User.ChangeStatus(UserStatus.Active);
        await e.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Sessions.RefreshAsync(first.RefreshToken!, Token));
        var second = await e.StartAsync(); e.Time.UtcNow = second.RefreshExpiresAt!.Value;
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => e.Sessions.RefreshAsync(second.RefreshToken!, Token));
    }
    [RealDbFact]
    public async Task Wrong_role_claim_and_unpersisted_session_cannot_authenticate()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var session = await e.StartAsync();
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, session.SessionId!.Value, e.User.SecurityVersion, "ADMIN", Token));
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, Guid.NewGuid(), e.User.SecurityVersion, "FARMER", Token));
    }
    [RealDbFact]
    public async Task Email_confirmation_sets_verified_once_and_is_bound_to_its_owner_and_purpose()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        var mail = Assert.Single(e.Messages.Sent);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ResetPasswordAsync(mail.Token, "new-password", Token));
        e.Current.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmEmailAsync(mail.Token, Token));
        e.Current.UserId = e.User.Id;
        await e.Challenges.ConfirmEmailAsync(mail.Token, Token);
        Assert.True(e.User.EmailVerified);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmEmailAsync(mail.Token, Token));
        Assert.DoesNotContain(await e.Context.AuditLogs.Where(a => a.EntityId == e.User.Id).Select(a => a.NewValues).ToListAsync(Token),
            value => value?.Contains(mail.Token) == true);
    }
    [RealDbFact]
    public async Task Password_reset_is_single_use_and_revokes_existing_tokens()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var session = await e.StartAsync(); var version = e.User.SecurityVersion;
        await e.Challenges.RequestResetAsync(e.User.Email!, Token);
        var mail = Assert.Single(e.Messages.Sent);
        await e.Challenges.ResetPasswordAsync(mail.Token, "reset-password", Token);
        Assert.Equal(AgriSage.Application.Common.Interfaces.PasswordVerification.Success, e.Passwords.Verify(e.User.PasswordHash, "reset-password"));
        Assert.False(await e.Validator.IsSessionAllowedAsync(e.User.Id, session.SessionId!.Value, version, "FARMER", Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ResetPasswordAsync(mail.Token, "another-password", Token));
    }
    [RealDbFact]
    public async Task Resend_cooldown_then_reissue_invalidates_the_predecessor()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        var first = Assert.Single(e.Messages.Sent);
        e.Time.UtcNow = e.Time.UtcNow.AddSeconds(61);
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        Assert.Equal(2, e.Messages.Sent.Count);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmEmailAsync(first.Token, Token));
        await e.Challenges.ConfirmEmailAsync(e.Messages.Sent[1].Token, Token);
    }
    [RealDbFact]
    public async Task Expired_or_contact_changed_email_challenge_is_rejected()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        var first = Assert.Single(e.Messages.Sent);
        e.User.UpdateContact($"{Guid.NewGuid():N}@example.test", e.User.PhoneNumber);
        await e.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmEmailAsync(first.Token, Token));
        e.Time.UtcNow = e.Time.UtcNow.AddSeconds(61);
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        e.Time.UtcNow = e.Messages.Sent[1].ExpiresAt;
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmEmailAsync(e.Messages.Sent[1].Token, Token));
    }
    [RealDbFact]
    public async Task Phone_otp_has_five_persisted_attempts_and_never_stores_plaintext()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.User.UpdateContact(e.User.Email, "09" + Random.Shared.Next(10_000_000, 99_999_999));
        await e.Context.SaveChangesAsync(Token);
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Sms, Token);
        var sms = Assert.Single(e.Messages.Sent);
        var wrong = sms.Token == "000000" ? "999999" : "000000";
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmPhoneAsync(wrong, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => e.Challenges.ConfirmPhoneAsync(sms.Token, Token));
        var challenge = await e.Context.AuthChallenges.SingleAsync(c => c.UserId == e.User.Id, Token);
        Assert.Equal(5, challenge.FailedAttempts); Assert.NotEqual(sms.Token, challenge.TokenHash);
    }
    [RealDbFact]
    public async Task Unknown_reset_accounts_do_not_send_messages_or_create_challenges()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        await e.Challenges.RequestResetAsync($"{Guid.NewGuid():N}@example.test", Token);
        Assert.Empty(e.Messages.Sent);
        Assert.False(await e.Context.AuthChallenges.AnyAsync(c => c.UserId == e.User.Id, Token));
    }
    [RealDbFact]
    public async Task Delivery_failure_invalidates_unsent_challenge_and_allows_retry()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        e.Messages.Fail = true;
        await Assert.ThrowsAsync<MessageDeliveryUnavailableException>(() => e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token));
        Assert.NotNull((await e.Context.AuthChallenges.SingleAsync(c => c.UserId == e.User.Id, Token)).ConsumedAt);
        e.Messages.Fail = false;
        await e.Challenges.RequestVerificationAsync(AuthDeliveryChannel.Email, Token);
        Assert.Single(e.Messages.Sent);
    }
}
