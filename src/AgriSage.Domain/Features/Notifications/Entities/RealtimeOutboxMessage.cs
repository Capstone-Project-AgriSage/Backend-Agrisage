using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Notifications.Entities;

// Transport metadata only. PostgreSQL triggers create these rows in the business transaction.
public sealed class RealtimeOutboxMessage : SoftDeletableEntity
{
    private RealtimeOutboxMessage() { }
    public string Topic { get; private set; } = null!;
    public string Scope { get; private set; } = null!;
    public Guid SubjectId { get; private set; }
    public long TransactionId { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastErrorCode { get; private set; }
    public void Complete(DateTimeOffset now) { ProcessedAt = now; LastErrorCode = null; }
    public void Retry(DateTimeOffset now)
    {
        Attempts++;
        LastErrorCode = "DISPATCH_FAILED";
        AvailableAt = now.AddSeconds(Math.Min(3600, Math.Pow(2, Math.Min(Attempts, 12))));
    }
}
