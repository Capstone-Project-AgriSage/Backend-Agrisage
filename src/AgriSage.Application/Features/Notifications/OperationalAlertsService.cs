using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

public sealed class OperationalAlertsService(IAgriSageDbContext context, NotificationWriter writer,
    IInventoryService inventory, IDateTimeProvider clock)
{
    public async Task DebtRemindersAsync(int batchSize, int withinDays, CancellationToken token)
    {
        var today = BusinessCalendar.Today(clock.UtcNow);
        var lastDay = today.AddDays(withinDays);
        var date = today.ToString("yyyyMMdd");
        var store = await ActiveStore.GetIdAsync(context, token);
        var candidates = await (from e in context.DebtEntries.AsNoTracking()
                                join a in context.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                                join f in context.FarmerProfiles.AsNoTracking() on a.FarmerProfileId equals f.Id
                                where a.StoreId == store && f.User.Status == UserStatus.Active && e.OutstandingAmount > 0 && e.DueDate <= lastDay
                                    && !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == f.UserId
                                        && n.DeduplicationKey == "debt:" + e.Id.ToString() + ":" + date)
                                orderby e.DueDate, e.Id
                                select new { e.Id, e.EntryNumber, e.DueDate, f.UserId }).Take(batchSize).ToListAsync(token);
        foreach (var e in candidates)
        {
            var overdue = e.DueDate < today;
            await writer.AddAsync([e.UserId], overdue ? "DEBT_OVERDUE" : "DEBT_DUE_SOON",
                overdue ? "Có khoản công nợ quá hạn" : "Có khoản công nợ đến hạn",
                $"Khoản công nợ {e.EntryNumber} có hạn thanh toán {e.DueDate:dd/MM/yyyy}. Vui lòng xem chi tiết công nợ.",
                $"debt:{e.Id}:{date}", "DEBT_ENTRY", e.Id, token);
        }
        await context.SaveChangesAsync(token);
    }

    public async Task InventoryAlertsAsync(int batchSize, int withinDays, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var recipients = await context.StoreMembers.AsNoTracking().Where(m => m.StoreId == store && m.Status == StoreMemberStatus.Active
            && (m.User.Role.Code == RoleCode.StoreOwner || m.User.Role.Code == RoleCode.SalesStaff)
            && m.User.Status == UserStatus.Active).Select(m => m.UserId).ToListAsync(token);
        if (recipients.Count == 0) return;
        var date = BusinessCalendar.Today(clock.UtcNow).ToString("yyyyMMdd");
        var page = 1;
        while (true)
        {
            var alerts = await inventory.GetAlertsAsync(new InventoryAlertsRequest
            { Page = page, PageSize = Math.Min(batchSize, 100), WithinDays = withinDays }, token);
            foreach (var alert in alerts.Items)
            {
                var entity = alert.InventoryLotId ?? alert.StoreProductId;
                var title = alert.Type == "LOW_STOCK" ? "Sản phẩm sắp hết hàng" : alert.Type == "EXPIRED" ? "Lô hàng đã hết hạn" : "Lô hàng sắp hết hạn";
                await writer.AddAsync(recipients, alert.Type == "LOW_STOCK" ? "LOW_STOCK" : "EXPIRY_WARNING", title,
                    $"{alert.ProductName} ({alert.Sku}): vui lòng kiểm tra tồn kho và lô hàng.",
                    $"inventory:{alert.Type}:{entity:N}:{date}", alert.InventoryLotId is null ? "STORE_PRODUCT" : "INVENTORY_LOT", entity, token);
            }
            // Each page is its own notification-only unit of work, never a midway business posting save.
            await context.SaveChangesAsync(token);
            if (page >= alerts.TotalPages) break;
            page++;
        }
    }
}
