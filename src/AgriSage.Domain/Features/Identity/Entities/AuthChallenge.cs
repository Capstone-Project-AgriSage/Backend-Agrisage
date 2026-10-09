using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class AuthChallenge : SoftDeletableEntity
{
    public const int MaxAttempts = 5;
    private AuthChallenge() { }

    public AuthChallenge(Guid userId, AuthChallengePurpose purpose, AuthDeliveryChannel channel,
        string destination, long securityVersion, DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty || securityVersion < 0 || !Enum.IsDefined(purpose) || !Enum.IsDefined(channel)
            || (purpose == AuthChallengePurpose.EmailVerification && channel != AuthDeliveryChannel.Email)
            || (purpose == AuthChallengePurpose.PhoneVerification && channel != AuthDeliveryChannel.Sms))
            throw new DomainException("Invalid authentication challenge.");
        UserId = userId;
        Purpose = purpose;
        Channel = channel;
        Destination = Guard.NotNullOrWhiteSpace(destination);
        SecurityVersion = securityVersion;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public AuthChallengePurpose Purpose { get; private set; }
    public AuthDeliveryChannel Channel { get; private set; }
    public string Destination { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public long SecurityVersion { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public int FailedAttempts { get; private set; }

    public void SetTokenHash(string tokenHash)
    {
        if (!string.IsNullOrEmpty(TokenHash)) throw new DomainException("A challenge token cannot be changed.");
        TokenHash = Guard.NotNullOrWhiteSpace(tokenHash);
    }

    public bool IsUsable(DateTimeOffset now, long securityVersion, string? destination) =>
        !IsDeleted && ConsumedAt is null && ExpiresAt > now && FailedAttempts < MaxAttempts
        && SecurityVersion == securityVersion && Destination == destination;

    public void FailAttempt()
    {
        if (FailedAttempts < MaxAttempts) FailedAttempts++;
    }

    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now, SecurityVersion, Destination)) throw new DomainException("The challenge is no longer usable.");
        ConsumedAt = now;
    }

    public void Invalidate(DateTimeOffset now) => ConsumedAt ??= now;
}
