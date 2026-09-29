using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Notifications.Enums;

namespace AgriSage.Domain.Features.Notifications.Entities;

// In-app notification. data holds navigation metadata only; no business truth lives only here.
// Only the documented invariant is enforced (READ → read_at); no transition graph is defined.
public sealed class Notification : SoftDeletableEntity
{
    private Notification()
    {
    }

    public Notification(Guid userId, string notificationType, string title, string message, string? data = null)
    {
        UserId = userId;
        NotificationType = Guard.NotNullOrWhiteSpace(notificationType);
        Title = Guard.NotNullOrWhiteSpace(title);
        Message = Guard.NotNullOrWhiteSpace(message);
        Data = data;
        Status = NotificationStatus.Unread;
    }

    public Guid UserId { get; private set; }

    // Example values only in the database design (ORDER_STATUS_CHANGED, PAYMENT_CONFIRMED, ...).
    public string NotificationType { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string Message { get; private set; } = null!;

    // Raw JSON (jsonb).
    public string? Data { get; private set; }

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset readAt)
    {
        Status = NotificationStatus.Read;
        ReadAt = readAt;
    }

    public void Archive() => Status = NotificationStatus.Archived;
}
