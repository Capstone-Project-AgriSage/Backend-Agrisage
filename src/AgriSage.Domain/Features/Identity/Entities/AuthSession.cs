using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class AuthSession : SoftDeletableEntity
{
    private AuthSession() { }

    public AuthSession(Guid userId, long securityVersion, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty || securityVersion < 0 || expiresAt <= now)
            throw new DomainException("Invalid authentication session.");
        UserId = userId;
        SecurityVersion = securityVersion;
        LastUsedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }
    public long SecurityVersion { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsUsable(DateTimeOffset now, long securityVersion) =>
        !IsDeleted && RevokedAt is null && ExpiresAt > now && SecurityVersion == securityVersion;

    public void Touch(DateTimeOffset now)
    {
        if (!IsUsable(now, SecurityVersion)) throw new DomainException("The session is no longer active.");
        LastUsedAt = now;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevocationReason = Guard.NotNullOrWhiteSpace(reason);
    }
}
