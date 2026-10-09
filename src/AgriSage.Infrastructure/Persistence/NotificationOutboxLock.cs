using AgriSage.Application.Features.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

public sealed class NotificationOutboxLock(AgriSageDbContext context) : INotificationOutboxLock
{
    public async Task LockAsync(Guid id, CancellationToken token) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM notification_outbox WHERE id = {id} FOR UPDATE", token);
}
