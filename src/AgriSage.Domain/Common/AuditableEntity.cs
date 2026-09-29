namespace AgriSage.Domain.Common;

public abstract class AuditableEntity : BaseEntity
{
    // Technical timestamps, assigned by the persistence audit interceptor (Infrastructure), not by business code.
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
