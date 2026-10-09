using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Notifications.Enums;

namespace AgriSage.UnitTests.Domain.Features.Identity;

public sealed class AuthSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Contact_changes_clear_only_changed_verification_and_revoke_existing_sessions()
    {
        var user = new User(Guid.NewGuid(), "Farmer", "hash", "old@example.test", "0901234567");
        user.MarkEmailVerified(); user.MarkPhoneVerified();
        user.UpdateContact("new@example.test", "0901234567");
        Assert.False(user.EmailVerified); Assert.True(user.PhoneVerified); Assert.Equal(1, user.SecurityVersion);
        user.UpdateContact("new@example.test", "0901234567");
        Assert.Equal(1, user.SecurityVersion);
        user.UpdateContact("new@example.test", "0907654321");
        Assert.False(user.PhoneVerified); Assert.Equal(2, user.SecurityVersion);
    }
    [Fact]
    public void Lock_then_unlock_does_not_restore_old_security_version()
    {
        var user = new User(Guid.NewGuid(), "Farmer", "hash", "a@example.test", null);
        user.ChangeStatus(UserStatus.Locked); user.ChangeStatus(UserStatus.Active);
        Assert.Equal(2, user.SecurityVersion);
        user.ChangePasswordHash("new-hash"); user.AssignRole(Guid.NewGuid());
        Assert.Equal(4, user.SecurityVersion);
    }
    [Fact]
    public void Session_expiry_version_mismatch_and_revocation_each_block_usage()
    {
        var session = new AuthSession(Guid.NewGuid(), 1, Now, Now.AddDays(1));
        Assert.True(session.IsUsable(Now, 1)); Assert.False(session.IsUsable(Now, 2));
        Assert.False(session.IsUsable(Now.AddDays(1), 1));
        session.Revoke(Now, "LOGOUT"); session.Revoke(Now.AddMinutes(1), "REPLAY");
        Assert.False(session.IsUsable(Now, 1)); Assert.Equal("LOGOUT", session.RevocationReason);
        Assert.Throws<DomainException>(() => session.Touch(Now));
    }
    [Fact]
    public void Refresh_token_cannot_be_consumed_twice_or_at_expiry()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", Now.AddMinutes(1));
        token.Consume(Now);
        Assert.Throws<DomainException>(() => token.Consume(Now));
        var expired = new RefreshToken(Guid.NewGuid(), "hash", Now);
        Assert.Throws<DomainException>(() => expired.Consume(Now));
    }
    [Fact]
    public void Challenges_are_bound_to_contact_version_expiry_and_five_attempts()
    {
        var challenge = new AuthChallenge(Guid.NewGuid(), AuthChallengePurpose.PhoneVerification,
            AuthDeliveryChannel.Sms, "0901234567", 2, Now.AddMinutes(10));
        challenge.SetTokenHash("hash");
        Assert.True(challenge.IsUsable(Now, 2, "0901234567"));
        Assert.False(challenge.IsUsable(Now, 3, "0901234567"));
        Assert.False(challenge.IsUsable(Now, 2, "0907654321"));
        Assert.False(challenge.IsUsable(Now.AddMinutes(10), 2, "0901234567"));
        for (var i = 0; i < 10; i++) challenge.FailAttempt();
        Assert.Equal(5, challenge.FailedAttempts); Assert.False(challenge.IsUsable(Now, 2, "0901234567"));
        Assert.Throws<DomainException>(() => challenge.Consume(Now));
    }
    [Fact]
    public void Email_challenge_is_single_use_and_cannot_replace_its_hash()
    {
        var challenge = new AuthChallenge(Guid.NewGuid(), AuthChallengePurpose.EmailVerification,
            AuthDeliveryChannel.Email, "a@example.test", 0, Now.AddMinutes(10));
        challenge.SetTokenHash("hash");
        Assert.Throws<DomainException>(() => challenge.SetTokenHash("replacement"));
        challenge.Consume(Now);
        Assert.False(challenge.IsUsable(Now, 0, "a@example.test"));
        Assert.Throws<DomainException>(() => challenge.Consume(Now));
    }
    [Fact]
    public void Verification_purpose_cannot_use_the_wrong_channel()
    {
        Assert.Throws<DomainException>(() => new AuthChallenge(Guid.NewGuid(), AuthChallengePurpose.EmailVerification,
            AuthDeliveryChannel.Sms, "0901234567", 0, Now.AddMinutes(10)));
    }
    [Fact]
    public void Notification_read_is_idempotent_and_archive_is_terminal()
    {
        var notification = new Notification(Guid.NewGuid(), "SYSTEM", "Title", "Message");
        notification.MarkRead(Now); notification.MarkRead(Now.AddMinutes(1));
        Assert.Equal(Now, notification.ReadAt);
        notification.Archive(); notification.MarkRead(Now.AddMinutes(2));
        Assert.Equal(NotificationStatus.Archived, notification.Status);
    }
    [Fact]
    public void Outbox_retry_delays_are_bounded_and_completion_is_idempotent()
    {
        var item = new NotificationOutbox(Guid.NewGuid(), Now);
        for (var i = 0; i < 20; i++) item.Retry(Now, "DISPATCH_FAILED");
        Assert.Equal(Now.AddHours(1), item.AvailableAt);
        item.Complete(Now); item.Complete(Now.AddMinutes(1));
        Assert.Equal(Now, item.ProcessedAt); Assert.Null(item.LastErrorCode);
    }
}
