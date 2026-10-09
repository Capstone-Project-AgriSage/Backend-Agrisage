using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class RefreshToken : SoftDeletableEntity
{
    private RefreshToken() { }

    public RefreshToken(Guid sessionId, string tokenHash, DateTimeOffset expiresAt)
    {
        if (sessionId == Guid.Empty) throw new DomainException("A refresh token requires a session.");
        SessionId = sessionId;
        TokenHash = Guard.NotNullOrWhiteSpace(tokenHash);
        ExpiresAt = expiresAt;
    }

    public Guid SessionId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public void Consume(DateTimeOffset now)
    {
        if (IsDeleted || ConsumedAt is not null || ExpiresAt <= now)
            throw new DomainException("The refresh token is no longer usable.");
        ConsumedAt = now;
    }
}
