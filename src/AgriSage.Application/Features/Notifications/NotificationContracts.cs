using System.Text.Json;
using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Notifications;

public sealed record NotificationListRequest : PaginationRequest { public string? Status { get; init; } }
public sealed record NotificationResponse(Guid Id, string NotificationType, string Title, string Message,
    JsonElement? Data, string Status, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);
public sealed record UnreadCountResponse(long Count);

public interface INotificationService
{
    Task<PagedResult<NotificationResponse>> ListAsync(NotificationListRequest request, CancellationToken token);
    Task<UnreadCountResponse> UnreadCountAsync(CancellationToken token);
    Task ReadAsync(Guid id, CancellationToken token);
    Task ReadAllAsync(CancellationToken token);
    Task ArchiveAsync(Guid id, CancellationToken token);
}

public interface INotificationOutboxLock { Task LockAsync(Guid id, CancellationToken token); }
