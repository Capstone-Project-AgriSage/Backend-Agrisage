using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

// Shared step; the caller saves once with its business change/outbox completion.
public sealed class NotificationWriter(IAgriSageDbContext context)
{
    public async Task AddAsync(IEnumerable<Guid> recipients, string type, string title, string message,
        string key, string entityType, Guid entityId, CancellationToken token, Guid? orderId = null)
    {
        var ids = recipients.Distinct().ToArray();
        var active = await context.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active
                && u.Role.IsActive && u.Role.DeletedAt == null)
            .Select(u => u.Id).ToListAsync(token);
        var delivered = await context.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => active.Contains(n.UserId) && n.DeduplicationKey == key).Select(n => n.UserId).ToListAsync(token);
        var data = JsonSerializer.Serialize(new { entityType, entityId, orderId });
        foreach (var id in active.Except(delivered))
            if (!context.Notifications.Local.Any(n => n.UserId == id && n.DeduplicationKey == key))
                context.Notifications.Add(new Notification(id, type, title, message, data, key));
    }
}
