using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Notifications.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

public sealed class NotificationService(IAgriSageDbContext context, ICurrentUserService currentUser,
    IDateTimeProvider clock, IAuthSecurityLock locks) : INotificationService
{
    public async Task<PagedResult<NotificationResponse>> ListAsync(NotificationListRequest request, CancellationToken token)
    {
        var userId = RequiredUser();
        var query = context.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumText.TryParse<NotificationStatus>(request.Status, out var status)) throw new BusinessRuleException("Invalid notification status.");
            query = query.Where(n => n.Status == status);
        }
        else query = query.Where(n => n.Status != NotificationStatus.Archived);
        var total = await query.LongCountAsync(token);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(n => new { n.Id, n.NotificationType, n.Title, n.Message, n.Data, n.Status, n.ReadAt, n.CreatedAt }).ToListAsync(token);
        return new(rows.Select(n => new NotificationResponse(n.Id, n.NotificationType, n.Title, n.Message,
            n.Data is null ? null : JsonSerializer.Deserialize<JsonElement>(n.Data), EnumText.Format(n.Status), n.ReadAt, n.CreatedAt)).ToList(),
            request.Page, request.PageSize, total);
    }
    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken token)
    {
        var id = RequiredUser();
        return new(await context.Notifications.AsNoTracking().LongCountAsync(n => n.UserId == id && n.Status == NotificationStatus.Unread, token));
    }
    public async Task ReadAsync(Guid id, CancellationToken token)
    {
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(RequiredUser(), token);
        var notification = await OwnAsync(id, token);
        notification.MarkRead(clock.UtcNow);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
    public async Task ReadAllAsync(CancellationToken token)
    {
        var id = RequiredUser();
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(id, token);
        foreach (var n in await context.Notifications.Where(n => n.UserId == id && n.Status == NotificationStatus.Unread).ToListAsync(token))
            n.MarkRead(clock.UtcNow);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
    public async Task ArchiveAsync(Guid id, CancellationToken token)
    {
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(RequiredUser(), token);
        (await OwnAsync(id, token)).Archive();
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }
    private async Task<Notification> OwnAsync(Guid id, CancellationToken token)
    {
        var userId = RequiredUser();
        return await context.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, token)
            ?? throw new NotFoundException("Notification", id);
    }
    private Guid RequiredUser() => currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
}
