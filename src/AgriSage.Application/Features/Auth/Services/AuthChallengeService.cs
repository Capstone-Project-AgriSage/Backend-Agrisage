using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class AuthChallengeService(IAgriSageDbContext context, ISecretTokenService secrets,
    IAuthMessageSender sender, IAuthSecurityLock locks, ICurrentUserService currentUser,
    IPasswordHashService passwords, IDateTimeProvider clock, IOptions<AuthSecurityOptions> options,
    AuditTrail audit) : IAuthChallengeService
{
    public async Task RequestResetAsync(string identifier, CancellationToken token)
    {
        var isEmail = identifier.Contains('@');
        var channel = isEmail ? AuthDeliveryChannel.Email : AuthDeliveryChannel.Sms;
        EnsureConfigured(channel); // same response for configured/unknown accounts; no delivery bypass
        var email = isEmail ? ContactNormalizer.NormalizeEmail(identifier) : null;
        var phone = !isEmail && ContactNormalizer.TryNormalizePhone(identifier, out var normalized) ? normalized : null;
        var userId = await context.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
                && ((email != null && u.Email != null && u.Email.ToLower() == email) || (phone != null && u.PhoneNumber == phone)))
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(token);
        if (userId is null) return;
        try { await RequestAsync(userId.Value, AuthChallengePurpose.PasswordReset, channel, token); }
        catch (MessageDeliveryUnavailableException)
        {
            // Anonymous reset acknowledgements must not reveal account existence when the provider is down.
            // RequestAsync invalidates the unsent challenge; the response acknowledges the request, not delivery.
        }
    }

    public async Task RequestVerificationAsync(AuthDeliveryChannel channel, CancellationToken token)
    {
        EnsureConfigured(channel);
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        await RequestAsync(userId, channel == AuthDeliveryChannel.Email
            ? AuthChallengePurpose.EmailVerification : AuthChallengePurpose.PhoneVerification, channel, token);
    }

    private async Task RequestAsync(Guid userId, AuthChallengePurpose purpose, AuthDeliveryChannel channel, CancellationToken token)
    {
        AuthChallenge? challenge = null;
        string? raw = null;
        // Provider I/O is after commit, so SMTP latency never holds a user row lock.
        await using (var tx = await context.BeginTransactionAsync(token))
        {
            await locks.LockUserAsync(userId, token);
            var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, token);
            if (user is null) return;
            var destination = Destination(user, channel);
            if (destination is null)
            {
                if (purpose == AuthChallengePurpose.PasswordReset) return;
                throw new BusinessRuleException("The account has no contact for this verification channel.");
            }
            if ((purpose == AuthChallengePurpose.EmailVerification && user.EmailVerified)
                || (purpose == AuthChallengePurpose.PhoneVerification && user.PhoneVerified)) return;

            var now = clock.UtcNow;
            var previous = await context.AuthChallenges.Where(c => c.UserId == userId && c.Purpose == purpose
                && c.ConsumedAt == null).OrderByDescending(c => c.CreatedAt).ToListAsync(token);
            if (previous.Any(c => c.CreatedAt > now.AddSeconds(-options.Value.ResendCooldownSeconds))) return;
            foreach (var old in previous) old.Invalidate(now);

            challenge = new AuthChallenge(userId, purpose, channel, destination, user.SecurityVersion,
                now.AddMinutes(options.Value.ChallengeMinutes));
            raw = purpose == AuthChallengePurpose.PhoneVerification ? secrets.GenerateOtp() : secrets.GenerateToken();
            challenge.SetTokenHash(secrets.HashChallenge(challenge.Id, raw));
            context.AuthChallenges.Add(challenge);
            await context.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }

        var deliveredToken = purpose == AuthChallengePurpose.PhoneVerification ? raw! : $"{challenge.Id:N}.{raw}";
        try
        {
            await sender.SendAsync(new AuthMessage(channel, challenge.Destination, purpose, deliveredToken, challenge.ExpiresAt), token);
        }
        catch (MessageDeliveryUnavailableException)
        {
            // A failed send must not leave an unsent usable challenge blocking the user's retry.
            await using var tx = await context.BeginTransactionAsync(token);
            await locks.LockUserAsync(userId, token);
            challenge.Invalidate(clock.UtcNow);
            audit.Record("AUTH_MESSAGE_DELIVERY_FAILED", "USER", userId, null);
            await context.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            throw;
        }
    }

    public Task ResetPasswordAsync(string tokenValue, string newPassword, CancellationToken token) =>
        ConfirmAsync(Parse(tokenValue).Id, Parse(tokenValue).Secret, AuthChallengePurpose.PasswordReset, newPassword, token);

    public Task ConfirmEmailAsync(string tokenValue, CancellationToken token) =>
        ConfirmAsync(Parse(tokenValue).Id, Parse(tokenValue).Secret, AuthChallengePurpose.EmailVerification, null, token);

    public async Task ConfirmPhoneAsync(string code, CancellationToken token)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var id = await context.AuthChallenges.AsNoTracking().Where(c => c.UserId == userId
                && c.Purpose == AuthChallengePurpose.PhoneVerification && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(token);
        if (id is null) throw InvalidChallenge();
        await ConfirmAsync(id.Value, code, AuthChallengePurpose.PhoneVerification, null, token);
    }

    private async Task ConfirmAsync(Guid id, string secret, AuthChallengePurpose purpose, string? password, CancellationToken token)
    {
        var targetUser = await context.AuthChallenges.AsNoTracking().Where(c => c.Id == id && c.Purpose == purpose)
            .Select(c => (Guid?)c.UserId).SingleOrDefaultAsync(token);
        if (targetUser is null || (purpose != AuthChallengePurpose.PasswordReset && targetUser != currentUser.UserId))
            throw InvalidChallenge();

        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(targetUser.Value, token);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == targetUser && u.Status == UserStatus.Active, token);
        var challenge = await context.AuthChallenges.SingleOrDefaultAsync(c => c.Id == id && c.Purpose == purpose, token);
        if (user is null || challenge is null
            || !challenge.IsUsable(clock.UtcNow, user.SecurityVersion, Destination(user, challenge.Channel)))
            throw InvalidChallenge();

        if (!secrets.VerifyChallenge(id, secret, challenge.TokenHash))
        {
            challenge.FailAttempt();
            await context.SaveChangesAsync(token);
            await tx.CommitAsync(token); // failed-attempt budget must survive the rejected confirmation
            throw InvalidChallenge();
        }

        challenge.Consume(clock.UtcNow);
        if (purpose == AuthChallengePurpose.PasswordReset)
        {
            user.ChangePasswordHash(passwords.Hash(password!));
            audit.Record("PASSWORD_RESET", "USER", user.Id, null);
        }
        else if (purpose == AuthChallengePurpose.EmailVerification)
        {
            user.MarkEmailVerified();
            audit.Record("EMAIL_VERIFIED", "USER", user.Id, null);
        }
        else
        {
            user.MarkPhoneVerified();
            audit.Record("PHONE_VERIFIED", "USER", user.Id, null);
        }
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    private void EnsureConfigured(AuthDeliveryChannel channel)
    {
        if (!sender.IsConfigured(channel)) throw new MessageDeliveryUnavailableException();
    }
    private static string? Destination(User user, AuthDeliveryChannel channel) =>
        channel == AuthDeliveryChannel.Email ? user.Email : user.PhoneNumber;
    private static (Guid Id, string Secret) Parse(string value)
    {
        var parts = value.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id) || parts[1].Length != 43)
            throw InvalidChallenge();
        return (id, parts[1]);
    }
    private static BusinessRuleException InvalidChallenge() => new("The verification token is invalid or expired.");
}
