using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Notifications.Entities;

public sealed class DebtSmsOutbox : SoftDeletableEntity
{
    private DebtSmsOutbox() { }

    public DebtSmsOutbox(Guid debtAccountId, string deduplicationKey, string phoneNumber, string message,
        string milestone, DateOnly dueDate, Guid? collectionCropSeasonId, DateTimeOffset now)
    {
        DebtAccountId = debtAccountId;
        DeduplicationKey = Guard.NotNullOrWhiteSpace(deduplicationKey);
        PhoneNumber = Guard.NotNullOrWhiteSpace(phoneNumber);
        Message = Guard.NotNullOrWhiteSpace(message);
        Milestone = Guard.NotNullOrWhiteSpace(milestone);
        DueDate = dueDate;
        CollectionCropSeasonId = collectionCropSeasonId;
        AvailableAt = now;
    }

    public Guid DebtAccountId { get; private set; }
    public string DeduplicationKey { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string Milestone { get; private set; } = null!;
    public DateOnly DueDate { get; private set; }
    public Guid? CollectionCropSeasonId { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastErrorCode { get; private set; }

    public void Sent(DateTimeOffset now) { SentAt ??= now; LastErrorCode = null; }
    public void Cancel(DateTimeOffset now) { CancelledAt ??= now; LastErrorCode = null; }
    public void RefreshPhone(string phoneNumber) => PhoneNumber = Guard.NotNullOrWhiteSpace(phoneNumber);
    public void RefreshMessage(string message) => Message = Guard.NotNullOrWhiteSpace(message);
    public void Retry(DateTimeOffset now)
    {
        Attempts++;
        LastErrorCode = "SMS_SEND_FAILED";
        AvailableAt = now.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(Attempts, 7))));
    }
}
