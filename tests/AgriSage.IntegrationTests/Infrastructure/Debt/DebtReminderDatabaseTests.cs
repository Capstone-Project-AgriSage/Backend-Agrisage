using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.18 against real PostgreSQL: debt reminders at the D10 milestones. A pickup debt of the fixture is due on 04/11/2026
// (30-day term, no season): N2 28/10, N3 04/11, N4 18/11, N5 04/12 with the default policy (−7, 0, +14, +30).
public sealed class DebtReminderDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly Due = new(2026, 11, 4);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task RemindAsync(CreditTestDatabase e, int batch = 100) =>
        Run(e, async sp => { await ReminderStartHelper.EnsureAsync(sp.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token); await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(batch, Token); return 0; });

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, DateOnly day) =>
        e.Clock.UtcNow = new DateTimeOffset(day.Year, day.Month, day.Day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> DebtAsync(CreditTestDatabase e, long quantity = 1)
    {
        var order = await e.FulfillAsync(quantity, Token, withoutSeason: true);
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id, Token)).Id;
    }

    private static async Task<List<(string Key, string Type, string Title)>> FarmerNoticesAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.UserId == e.FarmerUserId && n.DeduplicationKey!.StartsWith("debt-reminder:"))
                .Select(n => new { n.DeduplicationKey, n.NotificationType, n.Title }).ToListAsync(Token))
            .Select(n => (n.DeduplicationKey!, n.NotificationType, n.Title)).OrderBy(n => n.Item1).ToList();
    }

    private static async Task<List<string>> KeysAsync(CreditTestDatabase e, Guid user)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.UserId == user && n.DeduplicationKey!.StartsWith("debt-reminder")).Select(n => n.DeduplicationKey!).ToListAsync(Token))
            .Order().ToList();
    }

    private string Key(CreditTestDatabase e, DateOnly due, string milestone) => $"debt-reminder:{e.FarmerId}:{due:yyyyMMdd}:{milestone}";

    private static async Task<(decimal Balance, int Ledger)> LedgerAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return ((await db.DebtAccounts.AsNoTracking().SingleAsync(Token)).CurrentBalance, await db.DebtTransactions.CountAsync(Token));
    }

    private static async Task<Guid> SalesAsync(CreditTestDatabase e) =>
        (await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant)).UserId;

    // Run every day from 20 days before the due date to 36 days after it: a reminder appears only on N2, N3, N4 and N5,
    // twice the same day changes nothing, staff get one summary at N3 and N4, the owner one more at N5.
    [CreditDbFact]
    public async Task Daily_runs_send_only_the_milestones()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await DebtAsync(e, 2);
        var sales = await SalesAsync(e);
        var ledger = await LedgerAsync(e);
        var expected = new Dictionary<DateOnly, string>
        {
            [Due.AddDays(-7)] = "N2", [Due] = "N3", [Due.AddDays(14)] = "N4", [Due.AddDays(30)] = "N5",
        };

        var seen = new List<(DateOnly Day, string Key)>();
        for (var day = Due.AddDays(-20); day <= Due.AddDays(36); day = day.AddDays(1))
        {
            At(e, day);
            var before = (await FarmerNoticesAsync(e)).Count;
            await RemindAsync(e);
            await RemindAsync(e);
            var now = await FarmerNoticesAsync(e);
            Assert.Equal(expected.ContainsKey(day) ? before + 1 : before, now.Count);
            if (expected.TryGetValue(day, out var milestone)) seen.Add((day, Key(e, Due, milestone)));
        }

        var notices = await FarmerNoticesAsync(e);
        Assert.Equal(seen.Select(s => s.Key).Order(), notices.Select(n => n.Key));
        Assert.Equal(("DEBT_DUE_SOON", "Sắp đến hạn trả nợ"), notices.Where(n => n.Key.EndsWith("N2")).Select(n => (n.Type, n.Title)).Single());
        Assert.Equal(("DEBT_DUE_SOON", "Hôm nay là hạn trả nợ"), notices.Where(n => n.Key.EndsWith("N3")).Select(n => (n.Type, n.Title)).Single());
        Assert.Equal("DEBT_OVERDUE", notices.Single(n => n.Key.EndsWith("N4")).Type);
        Assert.Equal("DEBT_OVERDUE", notices.Single(n => n.Key.EndsWith("N5")).Type);

        Assert.Equal(new[] { $"debt-reminder-staff:{Due:yyyyMMdd}:N3", $"debt-reminder-staff:{Due.AddDays(14):yyyyMMdd}:N4" }, await KeysAsync(e, sales));
        Assert.Equal(new[]
        {
            $"debt-reminder-staff:{Due:yyyyMMdd}:N3", $"debt-reminder-staff:{Due.AddDays(14):yyyyMMdd}:N4",
            $"debt-reminder-staff:{Due.AddDays(30):yyyyMMdd}:N5",
        }, await KeysAsync(e, e.User.UserId!.Value));
        Assert.Equal(ledger, await LedgerAsync(e));
    }

    // A job that did not run for 20 days sends one reminder, the latest milestone reached, never a backlog.
    [CreditDbFact]
    public async Task After_a_pause_only_the_latest_milestone_is_sent()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await DebtAsync(e);
        At(e, Due.AddDays(-8));
        await RemindAsync(e);
        Assert.Empty(await FarmerNoticesAsync(e));

        At(e, Due.AddDays(20));
        await RemindAsync(e);
        Assert.Equal([Key(e, Due, "N4")], (await FarmerNoticesAsync(e)).Select(n => n.Key));
    }

    [CreditDbFact]
    public async Task Disputed_and_paid_debt_is_not_reminded()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var disputed = await DebtAsync(e);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "DISPUTE", "Sai số lượng", null, null, Token));
        At(e, Due);
        await RemindAsync(e);
        Assert.Empty(await FarmerNoticesAsync(e));

        // Once kept, the dispute no longer stops reminders; paid in full, they stop.
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "KEEP", "Đúng số lượng", null, null, Token));
        await RemindAsync(e);
        Assert.Equal([Key(e, Due, "N3")], (await FarmerNoticesAsync(e)).Select(n => n.Key));
        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 1_000_000m, FarmerProfileId: e.FarmerId), Token));
        At(e, Due.AddDays(14));
        await RemindAsync(e);
        Assert.Single(await FarmerNoticesAsync(e));
    }

    // A rollover moves the due date, so the schedule follows the new one; N1 comes on the harvest start of the season the debt
    // is now collected in.
    [CreditDbFact]
    public async Task After_a_rollover_the_reminders_follow_the_new_season()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(new("DX-2026", "Đông Xuân 2026–2027",
            new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token));
        var entry = await DebtAsync(e, 3);
        At(e, Due);
        await RemindAsync(e);
        await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, new("MAT_MUA", "Mất mùa", dx.Id), Token));

        At(e, Due.AddDays(14));                                  // the old N4: nothing, the debt is no longer due then
        await RemindAsync(e);
        At(e, new DateOnly(2027, 2, 14));
        await RemindAsync(e);
        Assert.Equal([Key(e, Due, "N3")], (await FarmerNoticesAsync(e)).Select(n => n.Key));

        At(e, new DateOnly(2027, 2, 15));                        // harvest start: N1 of Đông Xuân
        await RemindAsync(e);
        var n1 = Assert.Single(await FarmerNoticesAsync(e), n => n.Key.EndsWith(":N1"));
        Assert.Equal(($"debt-reminder:{e.FarmerId}:{dx.Id}:N1", "Vụ Đông Xuân 2026–2027 bắt đầu thu hoạch"), (n1.Key, n1.Title));
        At(e, new DateOnly(2027, 4, 7));                         // N2 of the new due date 14/04/2027
        await RemindAsync(e);
        Assert.Contains(Key(e, new DateOnly(2027, 4, 14), "N2"), (await FarmerNoticesAsync(e)).Select(n => n.Key));
        Assert.Equal(3, (await FarmerNoticesAsync(e)).Count);
    }

    // N1 is per (Farmer, season): one reminder for all the debt collected in the season, none for the debt of another one.
    [CreditDbFact]
    public async Task N1_reminds_each_season_once_on_its_harvest_start()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var seasons = new Dictionary<string, CropSeasonResponse>();
        using (var s = e.Scope())
        {
            var service = s.ServiceProvider.GetRequiredService<ICropSeasonService>();
            seasons["DX"] = await service.CreateAsync(new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
            seasons["HT"] = await service.CreateAsync(new("HT-2027", "Hè Thu", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token);
        }
        var a = await DebtAsync(e, 2);
        var b = await DebtAsync(e, 3);
        var c = await DebtAsync(e, 1);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            foreach (var (id, season) in new[] { (a, seasons["DX"]), (b, seasons["DX"]), (c, seasons["HT"]) })
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE debt_entries SET origin_crop_season_id = {season.Id}, collection_crop_season_id = {season.Id}, due_date = {season.PaymentDueDate}, original_due_date = {season.PaymentDueDate} WHERE id = {id}", Token);
        }

        At(e, new DateOnly(2027, 2, 14));
        await RemindAsync(e);
        Assert.Empty(await FarmerNoticesAsync(e));
        At(e, new DateOnly(2027, 2, 15));
        await RemindAsync(e);
        await RemindAsync(e);
        At(e, new DateOnly(2027, 2, 16));
        await RemindAsync(e);
        var notice = Assert.Single(await FarmerNoticesAsync(e));
        Assert.Equal($"debt-reminder:{e.FarmerId}:{seasons["DX"].Id}:N1", notice.Key);
        using (var s = e.Scope())
        {
            var message = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.AsNoTracking().IgnoreQueryFilters()
                .Where(n => n.DeduplicationKey == notice.Key).Select(n => n.Message).SingleAsync(Token);
            Assert.Contains("5.000.000 đ", message);
            Assert.Contains("14/04/2027", message);
        }
        // N1 is for the Farmer only.
        Assert.Empty(await KeysAsync(e, e.User.UserId!.Value));

        At(e, new DateOnly(2027, 7, 15));
        await RemindAsync(e);
        Assert.Contains($"debt-reminder:{e.FarmerId}:{seasons["HT"].Id}:N1", (await FarmerNoticesAsync(e)).Select(n => n.Key));
    }

    // The business date is Vietnam's: 17:00 UTC is midnight in Vietnam.
    [CreditDbFact]
    public async Task The_due_date_reminder_starts_at_midnight_Vietnam_time()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await DebtAsync(e);
        e.Clock.UtcNow = new DateTimeOffset(2026, 11, 3, 16, 59, 59, TimeSpan.Zero);
        await RemindAsync(e);
        Assert.Equal([Key(e, Due, "N2")], (await FarmerNoticesAsync(e)).Select(n => n.Key));
        e.Clock.UtcNow = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero);
        await RemindAsync(e);
        Assert.Contains(Key(e, Due, "N3"), (await FarmerNoticesAsync(e)).Select(n => n.Key));
    }

    // The offsets come from the store debt policy; small batches page through every customer.
    [CreditDbFact]
    public async Task The_store_policy_sets_the_offsets_and_small_batches_reach_everyone()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await DebtAsync(e);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(30, new DebtReminderOffsets(-3, 5, 10), 1, 2, 2, null, 365);
            db.StoreDebtPolicies.Add(policy);
            var role = await db.Roles.SingleAsync(r => r.Code == AgriSage.Domain.Features.Identity.Enums.RoleCode.Farmer, Token);
            for (var i = 0; i < 3; i++)
            {
                var user = new AgriSage.Domain.Features.Identity.Entities.User(role.Id, $"Farmer {i}", "hash", $"f{i}@example.test", null);
                var farmer = new AgriSage.Domain.Features.Customers.Entities.FarmerProfile(user.Id);
                var account = new DebtAccount(e.StoreId, farmer.Id);
                var posting = account.CreateManualAdjustmentEntry($"DE-F{i}", 100_000m, Due, e.User.UserId!.Value, e.Clock.UtcNow);
                db.AddRange(user, farmer, account, posting.Entry, posting.Transaction);
            }
            await db.SaveChangesAsync(Token);
        }

        At(e, Due.AddDays(-4));
        await RemindAsync(e, batch: 1);
        Assert.Empty(await FarmerNoticesAsync(e));
        At(e, Due.AddDays(-3));
        await RemindAsync(e, batch: 1);
        Assert.Equal([Key(e, Due, "N2")], (await FarmerNoticesAsync(e)).Select(n => n.Key));
        using (var s = e.Scope())
        {
            var count = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters()
                .CountAsync(n => n.DeduplicationKey!.EndsWith($"{Due:yyyyMMdd}:N2"), Token);
            Assert.Equal(4, count);
        }

        At(e, Due.AddDays(5));
        await RemindAsync(e, batch: 1);
        At(e, Due.AddDays(10));
        await RemindAsync(e, batch: 1);
        Assert.Equal(new[] { Key(e, Due, "N2"), Key(e, Due, "N4"), Key(e, Due, "N5") }.Order(), (await FarmerNoticesAsync(e)).Select(n => n.Key));
    }
}
