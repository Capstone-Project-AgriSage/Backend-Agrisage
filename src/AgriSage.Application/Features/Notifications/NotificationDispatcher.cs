using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

public sealed class NotificationDispatcher(IAgriSageDbContext context, INotificationOutboxLock locks,
    NotificationWriter writer, IDateTimeProvider clock)
{
    public Task<List<Guid>> DueAsync(int batchSize, CancellationToken token) => context.NotificationOutbox.AsNoTracking()
        .Where(e => e.ProcessedAt == null && e.AvailableAt <= clock.UtcNow).OrderBy(e => e.AvailableAt).ThenBy(e => e.Id)
        .Take(batchSize).Select(e => e.Id).ToListAsync(token);

    public async Task DispatchAsync(Guid id, CancellationToken token)
    {
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockAsync(id, token);
        var item = await context.NotificationOutbox.SingleOrDefaultAsync(e => e.Id == id, token);
        if (item is null || item.ProcessedAt is not null || item.AvailableAt > clock.UtcNow) return;
        var log = await context.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == item.AuditLogId, token);
        var recipients = await RecipientsAsync(log, token);
        var description = BusinessNotificationPolicy.Describe(log.Action);
        if (log.EntityId is { } entityId)
            await writer.AddAsync(recipients, description.Type, description.Title, description.Message,
                $"audit:{log.Id:N}", log.EntityType, entityId, token);
        item.Complete(clock.UtcNow);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    // Must be called in a fresh DI scope after a failed dispatch transaction has rolled back.
    public async Task RetryAsync(Guid id, CancellationToken token)
    {
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockAsync(id, token);
        var item = await context.NotificationOutbox.SingleOrDefaultAsync(e => e.Id == id, token);
        if (item is not null && item.ProcessedAt is null)
        {
            item.Retry(clock.UtcNow, "DISPATCH_FAILED");
            await context.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
    }

    private async Task<List<Guid>> RecipientsAsync(AuditLog log, CancellationToken token)
    {
        if (log.EntityId is not { } id || log.StoreId is not { } storeId) return [];
        if (log.Action == "DELIVERY_ASSIGNED")
        {
            // Use the assignment snapshot, so a later reassignment cannot notify the wrong employee.
            var values = log.NewValues is null ? default : JsonSerializer.Deserialize<JsonElement>(log.NewValues);
            if (values.ValueKind != JsonValueKind.Object || !values.TryGetProperty("assignedToMemberId", out var member)
                || !member.TryGetGuid(out var memberId)) return [];
            return await context.StoreMembers.AsNoTracking().Where(m => m.Id == memberId && m.StoreId == storeId
                && m.Status == StoreMemberStatus.Active).Select(m => m.UserId).ToListAsync(token);
        }

        Guid? farmerId = log.EntityType switch
        {
            "ORDER" => await context.Orders.AsNoTracking().Where(o => o.Id == id && o.StoreId == storeId)
                .Select(o => o.FarmerProfileId).SingleOrDefaultAsync(token),
            "PAYMENT" => await context.Payments.AsNoTracking().Where(p => p.Id == id && p.StoreId == storeId)
                .Select(p => p.PayerFarmerProfileId).SingleOrDefaultAsync(token),
            "DELIVERY" => await (from d in context.Deliveries.AsNoTracking()
                                  join o in context.Orders.AsNoTracking() on d.OrderId equals o.Id
                                  where d.Id == id && d.StoreId == storeId && o.StoreId == storeId
                                  select o.FarmerProfileId).SingleOrDefaultAsync(token),
            "CREDIT_PROFILE" => await context.FarmerCreditProfiles.AsNoTracking().Where(p => p.Id == id && p.StoreId == storeId)
                .Select(p => (Guid?)p.FarmerProfileId).SingleOrDefaultAsync(token),
            _ => null
        };
        if (farmerId is null) return [];
        return await context.FarmerProfiles.AsNoTracking().Where(f => f.Id == farmerId).Select(f => f.UserId).ToListAsync(token);
    }
}
