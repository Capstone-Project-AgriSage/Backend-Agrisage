using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Enums;
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
        var staff = await NotificationRecipients.StaffAsync(context, store, token, RoleCode.StoreOwner, RoleCode.SalesStaff);
        var candidates = await (from e in context.DebtEntries.AsNoTracking()
                                join a in context.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                                join f in context.FarmerProfiles.AsNoTracking() on a.FarmerProfileId equals f.Id
                                where a.StoreId == store && e.OutstandingAmount > 0 && e.DueDate <= lastDay
                                    && ((f.User.Status == UserStatus.Active && f.User.Role.IsActive
                                        && !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == f.UserId
                                            && n.DeduplicationKey == "debt:" + e.Id.ToString() + ":" + date))
                                        || context.Notifications.IgnoreQueryFilters().Count(n => staff.Contains(n.UserId)
                                            && n.DeduplicationKey == "debt:" + e.Id.ToString() + ":" + date) < staff.Count)
                                orderby e.DueDate, e.Id
                                select new { e.Id, e.EntryNumber, e.DueDate, f.UserId }).Take(batchSize).ToListAsync(token);
        foreach (var e in candidates)
        {
            var overdue = e.DueDate < today;
            await writer.AddAsync(staff.Append(e.UserId), overdue ? "DEBT_OVERDUE" : "DEBT_DUE_SOON",
                overdue ? "Có khoản công nợ quá hạn" : "Có khoản công nợ đến hạn",
                $"Khoản công nợ {e.EntryNumber} có hạn thanh toán {e.DueDate:dd/MM/yyyy}. Vui lòng xem chi tiết công nợ.",
                $"debt:{e.Id}:{date}", "DEBT_ENTRY", e.Id, token);
        }
        await context.SaveChangesAsync(token);
    }

    public async Task InventoryAlertsAsync(int batchSize, int withinDays, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var recipients = await NotificationRecipients.StaffAsync(context, store, token, RoleCode.StoreOwner, RoleCode.SalesStaff);
        if (recipients.Count == 0) return;
        var owners = await NotificationRecipients.StaffAsync(context, store, token, RoleCode.StoreOwner);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var date = today.ToString("yyyyMMdd");
        var levels = context.StoreProducts.AsNoTracking().Where(p => p.StoreId == store && p.IsActive && p.IsSellable && p.Product.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id, p.MinStockLevelBase, p.Product.Name, Sku = p.StoreSku ?? p.Product.Sku,
                Available = context.InventoryLots.Where(l => l.StoreProductId == p.Id && l.Status == InventoryLotStatus.Active
                    && (l.ExpiryDate == null || l.ExpiryDate >= today))
                    .Sum(l => (long?)(l.Balance.QuantityOnHand - l.Balance.QuantityReserved)) ?? 0
            }).Where(p => p.Available <= 0 || (p.MinStockLevelBase != null && p.Available < p.MinStockLevelBase));
        var page = 1;
        while (true)
        {
            var rows = await levels.OrderBy(p => p.Id).Skip((page - 1) * batchSize).Take(batchSize).ToListAsync(token);
            foreach (var row in rows)
            {
                var type = row.Available <= 0 ? "OUT_OF_STOCK" : "LOW_STOCK";
                await writer.AddAsync(recipients, type, row.Available <= 0 ? "Sản phẩm đã hết hàng" : "Sản phẩm sắp hết hàng",
                    $"{row.Name} ({row.Sku}): còn {row.Available} đơn vị cơ sở có thể bán. Vui lòng kiểm tra tồn kho.",
                    $"inventory:{type}:{row.Id:N}:{date}", "STORE_PRODUCT", row.Id, token);
            }
            await context.SaveChangesAsync(token);
            if (rows.Count < batchSize) break;
            page++;
        }
        if (owners.Count == 0) return;
        foreach (var type in new[] { "EXPIRING", "EXPIRED" })
        {
            page = 1;
            while (true)
            {
                var alerts = await inventory.GetAlertsAsync(new InventoryAlertsRequest
                { Page = page, PageSize = Math.Min(batchSize, 100), WithinDays = withinDays, Type = type }, token);
                foreach (var alert in alerts.Items)
                {
                    var entity = alert.InventoryLotId!.Value;
                    await writer.AddAsync(owners, "EXPIRY_WARNING", type == "EXPIRED" ? "Lô hàng đã hết hạn" : "Lô hàng sắp hết hạn",
                        $"{alert.ProductName} ({alert.Sku}), lô {alert.LotNumber}: vui lòng kiểm tra hạn sử dụng.",
                        $"inventory:{type}:{entity:N}:{date}", "INVENTORY_LOT", entity, token);
                }
                // Each page is its own notification-only unit of work; business ledgers stay untouched.
                await context.SaveChangesAsync(token);
                if (page >= alerts.TotalPages) break;
                page++;
            }
        }
    }
}
