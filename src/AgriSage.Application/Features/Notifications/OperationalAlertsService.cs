using System.Globalization;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Inventory;

using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

public sealed class OperationalAlertsService(IAgriSageDbContext context, NotificationWriter writer,
    IInventoryService inventory, IDateTimeProvider clock)
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    // F3.18 (D10, CS-09): debt reminders at fixed milestones instead of every day.
    //  N1 — the harvest start of the season the debt is collected in, per (Farmer, season), while its N2 has not come;
    //  N2..N5 — due date + offsets of the store debt policy (−7, 0, +14, +30 by default), per (Farmer, due date).
    // Each run sends only the latest milestone reached (a missed day never sends a backlog), at most once per milestone and
    // group (the deduplication key). Disputed, paid and cancelled entries are not reminded; a rollover changes the due date
    // and so the schedule. N3 and N4 also give each Sale staff member and owner one summary (in groups) per milestone and day; N5 gives
    // the owner one. Pages of customers are saved one by one; only notifications are written.
    public async Task DebtRemindersAsync(int batchSize, CancellationToken token)
    {
        var today = BusinessCalendar.Today(clock.UtcNow);
        var store = await ActiveStore.GetIdAsync(context, token);
        // F3.20: nothing is sent until the owner starts reminders (T0); milestones before T0 are never sent, checked by date at
        // every run (so a debt entered or given a season after the start cannot bring an old milestone back).
        var policy = await context.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token);
        if (policy?.RemindersStartDate is not { } start) return;
        var offsets = policy.ReminderOffsets;
        var seasons = (await context.CropSeasons.AsNoTracking()
                .Where(s => s.StoreId == store && s.ClosedAt == null && s.HarvestStart <= today && s.HarvestStart >= start).ToListAsync(token))
            .Where(s => today < s.PaymentDueDate.AddDays(offsets.N2)).ToDictionary(s => s.Id);

        var accounts = from a in context.DebtAccounts.AsNoTracking()
                       where a.StoreId == store && context.DebtEntries.Any(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0
                           && e.Status != DebtEntryStatus.Disputed && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled)
                       orderby a.Id
                       select new { a.Id, a.FarmerProfileId, a.FarmerProfile.UserId };
        for (var page = 0; ; page++)
        {
            var rows = await accounts.Skip(page * batchSize).Take(batchSize).ToListAsync(token);
            var ids = rows.Select(r => r.Id).ToList();
            var entries = await context.DebtEntries.AsNoTracking()
                .Where(e => ids.Contains(e.DebtAccountId) && e.OutstandingAmount > 0 && e.Status != DebtEntryStatus.Disputed
                    && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled)
                .Select(e => new { e.DebtAccountId, e.CollectionCropSeasonId, e.DueDate, e.OutstandingAmount }).ToListAsync(token);
            var users = rows.Select(r => r.UserId).ToList();
            var sent = (await context.Notifications.IgnoreQueryFilters().AsNoTracking()
                    .Where(n => users.Contains(n.UserId) && n.DeduplicationKey != null && n.DeduplicationKey.StartsWith("debt-reminder:"))
                    .Select(n => n.DeduplicationKey!).ToListAsync(token)).ToHashSet();

            foreach (var row in rows)
            {
                var own = entries.Where(e => e.DebtAccountId == row.Id).ToList();
                foreach (var group in own.Where(e => e.CollectionCropSeasonId is { } s && seasons.ContainsKey(s))
                             .GroupBy(e => e.CollectionCropSeasonId!.Value))
                {
                    var season = seasons[group.Key];
                    var key = $"debt-reminder:{row.FarmerProfileId}:{season.Id}:N1";
                    if (!sent.Add(key)) continue;
                    await writer.AddAsync([row.UserId], "DEBT_DUE_SOON", $"Vụ {season.Name} bắt đầu thu hoạch",
                        $"Bạn có {Money(group.Sum(e => e.OutstandingAmount))} nợ của vụ {season.Name}, hạn trả {season.PaymentDueDate:dd/MM/yyyy}.",
                        key, "DEBT_ACCOUNT", row.Id, token);
                }

                foreach (var group in own.GroupBy(e => e.DueDate))
                {
                    var due = group.Key;
                    if (DebtReminderSchedule.Latest(due, offsets, today, start) is not { } latest) continue;
                    var milestone = latest.Code;
                    var key = $"debt-reminder:{row.FarmerProfileId}:{due:yyyyMMdd}:{milestone}";
                    if (!sent.Add(key)) continue;
                    var amount = Money(group.Sum(e => e.OutstandingAmount));
                    var (title, message) = milestone switch
                    {
                        "N2" => ("Sắp đến hạn trả nợ", $"Khoản nợ {amount} đến hạn ngày {due:dd/MM/yyyy}."),
                        "N3" when due == today => ("Hôm nay là hạn trả nợ", $"Khoản nợ {amount} đến hạn hôm nay ({due:dd/MM/yyyy})."),
                        "N3" => ("Khoản nợ đã đến hạn", $"Khoản nợ {amount} đã đến hạn ngày {due:dd/MM/yyyy}."),
                        "N4" => ("Khoản nợ đã quá hạn", $"Khoản nợ {amount} đã quá hạn từ ngày {due:dd/MM/yyyy}. Vui lòng liên hệ cửa hàng."),
                        _ => ("Khoản nợ quá hạn lâu", $"Khoản nợ {amount} đã quá hạn từ ngày {due:dd/MM/yyyy}. Cửa hàng sẽ liên hệ với bạn."),
                    };
                    await writer.AddAsync([row.UserId], due < today ? "DEBT_OVERDUE" : "DEBT_DUE_SOON", title,
                        $"{message} Vui lòng xem chi tiết công nợ.", key, "DEBT_ACCOUNT", row.Id, token);
                }
            }

            await context.SaveChangesAsync(token);
            if (rows.Count < batchSize) break;
        }

        // The summaries are built from the group reminders SAVED per Vietnam day, not from what this run sent. Every (day,
        // milestone) of the last SummaryRecoveryDays days that has Farmer reminders but no summary yet gets one, with the numbers
        // of THAT day only. So a run that failed after saving the Farmer reminders but before the summaries is repaired by the
        // next run, also on a later day: nothing is sent to a Farmer twice, a summary that exists is never written again (its key
        // carries the day), and the recipients are the members who already existed at the end of that day.
        await WriteMissingSummariesAsync(store, offsets, today, start, token);
        await context.SaveChangesAsync(token);
    }

    // How far back a missing summary is still recovered (days). A value of this change — proposed, to be confirmed.
    internal const int SummaryRecoveryDays = 14;

    private async Task WriteMissingSummariesAsync(Guid store, DebtReminderOffsets offsets, DateOnly today, DateOnly start, CancellationToken token)
    {
        var from = today.AddDays(-SummaryRecoveryDays);
        if (start > from) from = start;
        var counts = await GroupRemindersPerDayAsync(from, today, token);
        if (counts.Count == 0) return;

        var keys = counts.Keys.Select(k => SummaryKey(k.Day, k.Milestone)).ToList();
        var written = (await context.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.DeduplicationKey != null && keys.Contains(n.DeduplicationKey)).Select(n => n.DeduplicationKey!).ToListAsync(token)).ToHashSet();
        foreach (var ((day, milestone), count) in counts.OrderBy(c => c.Key.Day).ThenBy(c => c.Key.Milestone, StringComparer.Ordinal))
        {
            var key = SummaryKey(day, milestone);
            if (written.Contains(key)) continue;
            // Today: the members now. An earlier day: the members who were there by the end of that day.
            var until = day == today ? (DateTimeOffset?)null : BusinessCalendar.StartOfDay(day.AddDays(1));
            var prefix = day == today ? "" : $"Ngày {day:dd/MM/yyyy}: ";
            if (milestone == "N5")
            {
                var owners = await NotificationRecipients.StaffAsync(context, store, until, token, RoleCode.StoreOwner);
                await writer.AddAsync(owners, "DEBT_OVERDUE", "Nợ quá hạn lâu cần quyết định",
                    $"{prefix}{count} nhóm nợ đã quá hạn {offsets.N5} ngày. Vui lòng xem mục Cần quyết định.", key, "STORE", store, token);
                continue;
            }

            var staff = await NotificationRecipients.StaffAsync(context, store, until, token, RoleCode.StoreOwner, RoleCode.SalesStaff);
            if (milestone == "N3")
                await writer.AddAsync(staff, "DEBT_DUE_SOON", "Nợ đến hạn trả",
                    $"{prefix}{count} nhóm nợ vừa đến hạn trả nợ. Vui lòng xem danh sách công nợ.", key, "STORE", store, token);
            else
                await writer.AddAsync(staff, "DEBT_OVERDUE", "Nợ quá hạn trả",
                    $"{prefix}{count} nhóm nợ đã quá hạn {offsets.N4} ngày. Vui lòng liên hệ nhắc nợ.", key, "STORE", store, token);
        }
    }

    private static string SummaryKey(DateOnly day, string milestone) => $"debt-reminder-staff:{day:yyyyMMdd}:{milestone}";

    // One Farmer reminder per (Farmer, due date) group and milestone: its key is debt-reminder:{farmer}:{yyyyMMdd}:N3..N5. They
    // are counted per Vietnam day in [start of the day, start of the next day), so a day never takes in another day's reminders.
    private async Task<Dictionary<(DateOnly Day, string Milestone), int>> GroupRemindersPerDayAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var begin = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to.AddDays(1));
        var rows = await context.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.CreatedAt >= begin && n.CreatedAt < end && n.DeduplicationKey != null && n.DeduplicationKey.StartsWith("debt-reminder:")
                && (n.DeduplicationKey.EndsWith(":N3") || n.DeduplicationKey.EndsWith(":N4") || n.DeduplicationKey.EndsWith(":N5")))
            .Select(n => new { n.DeduplicationKey, n.CreatedAt }).ToListAsync(token);
        return rows.GroupBy(r => (Day: BusinessCalendar.Today(r.CreatedAt), Milestone: r.DeduplicationKey![^2..]))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static string Money(decimal amount) => amount.ToString("N0", Vietnamese) + " đ";

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
