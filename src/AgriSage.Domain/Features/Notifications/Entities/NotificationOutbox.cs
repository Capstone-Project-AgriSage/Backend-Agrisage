using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Notifications.Entities;

public sealed class NotificationOutbox : SoftDeletableEntity
{
    private NotificationOutbox() { }
    public NotificationOutbox(Guid auditLogId, DateTimeOffset availableAt)
    {
        AuditLogId = auditLogId;
        AvailableAt = availableAt;
    }
    public Guid AuditLogId { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastErrorCode { get; private set; }
    public void Complete(DateTimeOffset now) { ProcessedAt ??= now; LastErrorCode = null; }
    public void Retry(DateTimeOffset now, string errorCode)
    {
        Attempts++;
        LastErrorCode = Guard.NotNullOrWhiteSpace(errorCode);
        AvailableAt = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(Attempts, 7))));
    }
}
