using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Audit.Entities;

// Append-only audit trail: created once, never updated or deleted (no DELETE or ordinary UPDATE API).
// A BaseEntity on purpose: the table has no audit or soft-delete columns.
// actor_user_id is null for system operations (payOS webhook, scheduled jobs).
public sealed class AuditLog : BaseEntity
{
    private AuditLog()
    {
    }

    public AuditLog(
        string action,
        string entityType,
        DateTimeOffset occurredAt,
        Guid? storeId = null,
        Guid? actorUserId = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? reason = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        Action = Guard.NotNullOrWhiteSpace(action);
        EntityType = Guard.NotNullOrWhiteSpace(entityType);
        OccurredAt = occurredAt;
        StoreId = storeId;
        ActorUserId = actorUserId;
        EntityId = entityId;
        OldValues = oldValues;
        NewValues = newValues;
        Reason = reason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
    }

    public Guid? StoreId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    // Example values only in the database design (PRICE_OVERRIDE, RETURN_APPROVED, ...).
    public string Action { get; private set; } = null!;

    public string EntityType { get; private set; } = null!;

    public Guid? EntityId { get; private set; }

    // Raw JSON (jsonb).
    public string? OldValues { get; private set; }

    // Raw JSON (jsonb).
    public string? NewValues { get; private set; }

    public string? Reason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    // Links the audit records written by one business transaction/request.
    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
