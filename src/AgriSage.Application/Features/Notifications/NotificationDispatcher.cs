using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
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
        var subject = await SubjectAsync(log, token);
        var recipients = subject is null ? [] : await RecipientsAsync(log, subject, token);
        var description = BusinessNotificationPolicy.Describe(log.Action, log.NewValues, subject?.DebtPayment == true);
        if (log.EntityId is { } entityId && subject is not null)
            await writer.AddAsync(recipients, description.Type, description.Title, description.Message,
                $"audit:{log.Id:N}", log.EntityType, entityId, token, subject.OrderId);
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

    private sealed record Subject(Guid? FarmerId, Guid? OrderId, bool DebtPayment = false, bool FarmerCheckout = false);

    private async Task<Subject?> SubjectAsync(AuditLog log, CancellationToken token)
    {
        if (log.EntityId is not { } id || log.StoreId is not { } storeId) return null;
        switch (log.EntityType)
        {
            case "ORDER":
                return await context.Orders.AsNoTracking().Where(o => o.Id == id && o.StoreId == storeId)
                    .Select(o => new Subject(o.FarmerProfileId, o.Id, false, o.FarmerProfileId != null
                        && (o.Source == OrderSource.FarmerWeb || o.Source == OrderSource.FarmerMobile))).SingleOrDefaultAsync(token);
            case "PAYMENT":
                return await context.Payments.AsNoTracking().Where(p => p.Id == id && p.StoreId == storeId)
                    .Select(p => new Subject(p.PayerFarmerProfileId, p.OrderId, p.PaymentContext == PaymentContext.DebtRepayment, false))
                    .SingleOrDefaultAsync(token);
            case "DELIVERY":
                return await (from d in context.Deliveries.AsNoTracking()
                              join o in context.Orders.AsNoTracking() on d.OrderId equals o.Id
                              where d.Id == id && d.StoreId == storeId && o.StoreId == storeId
                              select new Subject(o.FarmerProfileId, o.Id, false, false)).SingleOrDefaultAsync(token);
            case "CREDIT_PROFILE":
                return await context.FarmerCreditProfiles.AsNoTracking().Where(p => p.Id == id && p.StoreId == storeId)
                    .Select(p => new Subject(p.FarmerProfileId, null, false, false)).SingleOrDefaultAsync(token);
            case "DEBT_ENTRY":
                return await (from e in context.DebtEntries.AsNoTracking()
                              join a in context.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                              where e.Id == id && a.StoreId == storeId
                              select new Subject(a.FarmerProfileId, e.OrderId, false, false)).SingleOrDefaultAsync(token);
            case "SALES_RETURN":
                return await context.SalesReturns.AsNoTracking().Where(r => r.Id == id && r.StoreId == storeId)
                    .Select(r => new Subject(r.FarmerProfileId, r.OrderId, false, false)).SingleOrDefaultAsync(token);
            case "REFUND":
                var refund = await context.Refunds.AsNoTracking().Where(r => r.Id == id && r.StoreId == storeId)
                    .Select(r => new { r.OrderId, r.SalesReturnId }).SingleOrDefaultAsync(token);
                if (refund?.OrderId is { } orderId)
                    return await context.Orders.AsNoTracking().Where(o => o.Id == orderId && o.StoreId == storeId)
                        .Select(o => new Subject(o.FarmerProfileId, o.Id, false, false)).SingleOrDefaultAsync(token);
                if (refund?.SalesReturnId is { } returnId)
                    return await context.SalesReturns.AsNoTracking().Where(r => r.Id == returnId && r.StoreId == storeId)
                        .Select(r => new Subject(r.FarmerProfileId, r.OrderId, false, false)).SingleOrDefaultAsync(token);
                return null;
            default: return null;
        }
    }

    private async Task<List<Guid>> RecipientsAsync(AuditLog log, Subject subject, CancellationToken token)
    {
        var storeId = log.StoreId!.Value;
        // Only Farmer checkout notifies the store team; counter sales do not create these alerts.
        if (log.Action == "ORDER_PLACED" && (log.EntityType != "ORDER" || !subject.FarmerCheckout)) return [];
        if (log.Action == "DELIVERY_ASSIGNED")
        {
            // Use the assignment snapshot, so a later reassignment cannot notify the wrong employee.
            var values = log.NewValues is null ? default : JsonSerializer.Deserialize<JsonElement>(log.NewValues);
            if (values.ValueKind != JsonValueKind.Object || !values.TryGetProperty("assignedToMemberId", out var member)
                || !member.TryGetGuid(out var memberId)) return [];
            return await context.StoreMembers.AsNoTracking().Where(m => m.Id == memberId && m.StoreId == storeId
                && m.Status == StoreMemberStatus.Active && m.User.Status == UserStatus.Active
                && m.User.Role.IsActive && m.User.Role.DeletedAt == null).Select(m => m.UserId).ToListAsync(token);
        }

        var owner = log.Action is "ORDER_PLACED" or "ORDER_CREATED" or "COUNTER_SALE_COMPLETED" or "ORDER_CANCELLED"
            or "ORDER_ITEM_REMAINING_CANCELLED" or "RETURN_REQUESTED" or "REFUND_PENDING"
            or "CUSTOMER_CREDIT_LIMIT_CHANGED" or "DEBT_DISPUTE"
            || (subject.DebtPayment && log.Action is ("PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED"));
        var sales = log.Action is "ORDER_PLACED" or "ORDER_CREATED" or "COUNTER_SALE_COMPLETED" or "ORDER_CANCELLED" or "ORDER_CONFIRMED"
            or "ORDER_ITEM_REMAINING_CANCELLED"
            or "ORDER_PREPARING_STARTED" or "ORDER_MARKED_READY" or "ORDER_PICKED_UP"
            or "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" or "PAYMENT_FAILED" or "DEBT_PAYMENT_REJECTED"
            or "RETURN_REQUESTED" or "REFUND_PENDING" or "DELIVERY_CREATED" or "DELIVERY_DISPATCHED"
            or "DELIVERY_ATTEMPT_COMPLETED" or "DELIVERY_CANCELLED" or "DEBT_DISPUTE";
        List<RoleCode> roles = [];
        if (owner) roles.Add(RoleCode.StoreOwner);
        if (sales) roles.Add(RoleCode.SalesStaff);
        var recipients = roles.Count == 0 ? [] : await NotificationRecipients.StaffAsync(context, storeId, token, [.. roles]);
        if (subject.FarmerId is { } farmerId && log.Action is not ("ORDER_PLACED" or "ORDER_CREATED" or "DELIVERY_CREATED" or "RETURN_REQUESTED" or "REFUND_PENDING" or "DEBT_DISPUTE"))
            recipients.AddRange(await context.FarmerProfiles.AsNoTracking().Where(f => f.Id == farmerId)
                .Select(f => f.UserId).ToListAsync(token));
        return recipients;
    }
}
