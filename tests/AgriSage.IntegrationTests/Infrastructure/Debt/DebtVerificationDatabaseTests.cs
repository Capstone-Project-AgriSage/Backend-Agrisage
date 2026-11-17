using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20: the verifications V1, V2, V4, V5 and V6 of the proposal (F3_7 §10) on real PostgreSQL. V3 (permissions over the real
// routes) is in OwnerOnlyDebtSeasonEndToEndTests. V1 also stands on tests that already exist: the payOS webhook delivered twice
// (PayOsPaymentDatabaseTests) and the return posting retried (CreditDebtDatabaseTests).
public sealed class DebtVerificationDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid ProfileId, Guid UserId)> FarmerAsync(CreditTestDatabase e, string name)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
        var user = new User(role.Id, name, "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        db.AddRange(user, farmer, new FarmerCreditProfile(e.StoreId, farmer.Id, 20_000_000m, e.User.UserId!.Value, e.Clock.UtcNow, e.TierId),
            new DebtAccount(e.StoreId, farmer.Id));
        await db.SaveChangesAsync(Token);
        return (farmer.Id, user.Id);
    }

    private static Task<DebtEntryResponse> ManualAsync(CreditTestDatabase e, Guid farmer, decimal amount, DateOnly due) =>
        Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(amount, due, "Đơn chịu"), Token));

    private static Task<DebtEntryResponse> ActionAsync(CreditTestDatabase e, Guid entry, string action, decimal? amount, DateOnly? due, string? code) =>
        Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(entry, action, "Ghi chú của chủ", amount, due, code, Token));

    private static async Task<(decimal Entries, decimal Balance, decimal Ledger, int Negative)> TotalsAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return (await db.DebtEntries.AsNoTracking().SumAsync(x => x.OutstandingAmount, Token),
            await db.DebtAccounts.AsNoTracking().SumAsync(x => x.CurrentBalance, Token),
            await db.DebtTransactions.AsNoTracking().Where(t => t.Status == DebtTransactionStatus.Posted).SumAsync(t => t.AmountDelta, Token),
            await db.DebtEntries.AsNoTracking().CountAsync(x => x.OutstandingAmount < 0, Token));
    }

    // V1: the same confirmation sent twice has one financial effect: one payment line, one allocation, one balance change.
    [CreditDbFact]
    public async Task V1_a_bank_transfer_confirmed_twice_counts_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var entry = (await ManualAsync(e, e.FarmerId, 5_000_000m, new DateOnly(2026, 12, 1))).Id;
        var payment = await Run(e, sp => sp.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 3_000_000m, Reference: "V1"), Token));
        Assert.Equal(5_000_000m, (await TotalsAsync(e)).Balance);                    // recorded, not confirmed: the debt is unchanged

        await Run(e, sp => sp.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(payment.Id, Token));
        try { await Run(e, sp => sp.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(payment.Id, Token)); }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException) { /* refused or ignored: both are fine */ }

        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        Assert.Equal(1, await db.DebtTransactions.CountAsync(t => t.TransactionType == DebtTransactionType.Payment, Token));
        Assert.Equal(1, await db.PaymentAllocations.CountAsync(Token));
        var totals = await TotalsAsync(e);
        Assert.Equal((2_000_000m, 2_000_000m, 2_000_000m), (totals.Entries, totals.Balance, totals.Ledger));
        Assert.Equal(2_000_000m, (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.Id == entry, Token)).OutstandingAmount);
    }

    // V1: a rollover sent twice to the same season moves the debt once: one ROLLOVER action, one rollover counted.
    [CreditDbFact]
    public async Task V1_a_rollover_sent_twice_counts_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using var s0 = e.Scope();
        var seasons = s0.ServiceProvider.GetRequiredService<ICropSeasonService>();
        var dx = await seasons.CreateAsync(new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var entry = (await ManualAsync(e, e.FarmerId, 1_000_000m, new DateOnly(2026, 11, 4))).Id;
        var request = new DebtRolloverRequest("MAT_MUA", "Mất mùa", dx.Id);

        await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, request, Token));
        await Assert.ThrowsAsync<DomainException>(() => Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, request, Token)));

        using var s = e.Scope();
        var stored = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().Include(x => x.Actions).SingleAsync(x => x.Id == entry, Token);
        Assert.Equal((1, 1), (stored.RolloverCount, stored.Actions.Count(a => a.ActionType == DebtEntryActionType.Rollover)));
    }

    // V2: a payment and a reduction of the same debt at the same moment never take more than is owed, and nothing is lost: the
    // entries, the account and the ledger always agree. Independent connections, repeated.
    [CreditDbFact]
    public async Task V2_a_payment_and_a_reduction_at_once_never_exceed_the_debt()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        for (var round = 0; round < 4; round++)
        {
            var (farmer, _) = await FarmerAsync(e, $"Khách {round}");
            var entry = (await ManualAsync(e, farmer, 1_000_000m, new DateOnly(2026, 12, 1))).Id;
            var outcomes = await Task.WhenAll(
                Attempt(() => Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 800_000m, FarmerProfileId: farmer), Token))),
                Attempt(() => ActionAsync(e, entry, "ADJUST", 800_000m, null, "TRI_AN")),
                Attempt(() => ActionAsync(e, entry, "ADJUST", 800_000m, null, "KHAC")));

            // At least one succeeds and the two reductions cannot both fit in 1,000,000.
            Assert.True(outcomes.Any(ok => ok), $"round {round}: nothing succeeded");
            Assert.False(outcomes[1] && outcomes[2], $"round {round}: both reductions of 800,000 were applied to 1,000,000");
            var totals = await TotalsAsync(e);
            Assert.Equal(0, totals.Negative);
            Assert.Equal(totals.Entries, totals.Balance);
            Assert.Equal(totals.Balance, totals.Ledger);
            // The entry's own ledger lines add up to what it still owes: no deduction without a line, no line without a deduction.
            using var s = e.Scope();
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var outstanding = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.Id == entry, Token)).OutstandingAmount;
            Assert.InRange(outstanding, 0m, 1_000_000m);
            Assert.Equal(outstanding, await db.DebtTransactions.AsNoTracking()
                .Where(t => t.DebtEntryId == entry && t.Status == DebtTransactionStatus.Posted).SumAsync(t => t.AmountDelta, Token));
        }
    }

    // V2 made deterministic: while another transaction holds the Farmer lock, every debt change waits for it. This is what keeps
    // two people from deducting from the same debt at once; a race between two requests rarely lines up in a test, so the lock
    // is held by hand and the changes are started behind it.
    [CreditDbFact]
    public async Task V2_every_debt_change_waits_for_the_farmer_lock()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        CropSeasonResponse dx;
        using (var s = e.Scope())
            dx = await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var due = new DateOnly(2026, 11, 4);
        var toAdjust = (await ManualAsync(e, e.FarmerId, 1_000_000m, due)).Id;
        var toCancel = (await ManualAsync(e, e.FarmerId, 1_000_000m, due)).Id;
        var toDispute = (await ManualAsync(e, e.FarmerId, 1_000_000m, due)).Id;
        var toRoll = (await ManualAsync(e, e.FarmerId, 1_000_000m, due)).Id;

        using var holder = e.Scope();
        var db = holder.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await holder.ServiceProvider.GetRequiredService<AgriSage.Application.Common.Interfaces.IRowLockService>().LockFarmerProfileAsync(e.FarmerId, Token);

        var started = new[]
        {
            Attempt(() => ActionAsync(e, toAdjust, "ADJUST", 100_000m, null, "TRI_AN")),
            Attempt(() => ActionAsync(e, toCancel, "CANCEL", null, null, "KHAC")),
            Attempt(() => ActionAsync(e, toDispute, "DISPUTE", null, null, null)),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 100_000m, FarmerProfileId: e.FarmerId), Token))),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(toRoll, new("MAT_MUA", "Mất mùa", dx.Id), Token))),
        };
        await Task.Delay(TimeSpan.FromSeconds(3), Token);
        Assert.All(started, t => Assert.False(t.IsCompleted, "a debt change did not wait for the Farmer lock"));

        await tx.CommitAsync(Token);                                                    // release the lock
        Assert.All(await Task.WhenAll(started), ok => Assert.True(ok));
        var totals = await TotalsAsync(e);
        Assert.Equal((totals.Entries, totals.Balance), (totals.Balance, totals.Ledger));
    }

    private static async Task<bool> Attempt(Func<Task> act)
    {
        try { await act(); return true; }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException or ConflictException) { return false; }
    }


    // V4: after several rollovers and a direct due-date change, the origin season, the original due date and the due date at any
    // moment still read from the entry and its actions.
    [CreditDbFact]
    public async Task V4_the_history_of_due_dates_is_readable_after_several_changes()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        CropSeasonResponse dx, ht, td;
        using (var s = e.Scope())
        {
            var seasons = s.ServiceProvider.GetRequiredService<ICropSeasonService>();
            dx = await seasons.CreateAsync(new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
            ht = await seasons.CreateAsync(new("HT-2027", "Hè Thu", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token);
            td = await seasons.CreateAsync(new("TD-2027", "Thu Đông", new DateOnly(2027, 11, 15), new DateOnly(2027, 12, 15), 30), Token);
        }

        At(e, 2026, 12, 6);
        var entry = (await ManualAsync(e, e.FarmerId, 1_000_000m, new DateOnly(2027, 4, 14))).Id;
        using (var s = e.Scope())
        {
            await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE debt_entries SET origin_crop_season_id = {dx.Id}, collection_crop_season_id = {dx.Id} WHERE id = {entry}", Token);
        }

        At(e, 2027, 4, 28);
        await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, new("MAT_MUA", "Lần một", ht.Id), Token));
        At(e, 2027, 9, 20);
        await ActionAsync(e, entry, "CHANGE_DUE_DATE", null, new DateOnly(2027, 10, 31), "KHO_KHAN");
        At(e, 2027, 10, 25);
        await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, new("THIEN_TAI", "Lần hai", td.Id), Token));

        using var verify = e.Scope();
        var stored = await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().Include(x => x.Actions).SingleAsync(x => x.Id == entry, Token);
        Assert.Equal((dx.Id, td.Id, new DateOnly(2027, 4, 14), new DateOnly(2028, 1, 14), 2),
            (stored.OriginCropSeasonId!.Value, stored.CollectionCropSeasonId!.Value, stored.OriginalDueDate, stored.DueDate, stored.RolloverCount));
        var actions = stored.Actions.Where(a => a.NewDueDate != null).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToList();
        Assert.Equal([DebtEntryActionType.Rollover, DebtEntryActionType.ChangeDueDate, DebtEntryActionType.Rollover], actions.Select(a => a.ActionType));
        // The chain is continuous: each change starts from where the previous one ended, from the original due date to today's.
        Assert.Equal(stored.OriginalDueDate, actions[0].OldDueDate);
        Assert.Equal(actions[0].NewDueDate, actions[1].OldDueDate);
        Assert.Equal(actions[1].NewDueDate, actions[2].OldDueDate);
        Assert.Equal(stored.DueDate, actions[2].NewDueDate);
        Assert.Equal((dx.Id, ht.Id, ht.Id, td.Id), (actions[0].OldCropSeasonId!.Value, actions[0].NewCropSeasonId!.Value, actions[2].OldCropSeasonId!.Value, actions[2].NewCropSeasonId!.Value));

        // The due date at a given moment: the last change made by then, else the original.
        DateOnly DueAt(DateTimeOffset moment) => actions.LastOrDefault(a => a.CreatedAt <= moment)?.NewDueDate ?? stored.OriginalDueDate;
        Assert.Equal(new DateOnly(2027, 4, 14), DueAt(new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 9, 14), DueAt(new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 10, 31), DueAt(new DateTimeOffset(2027, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2028, 1, 14), DueAt(new DateTimeOffset(2027, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        // The direct change is not a rollover, and the detail shows the same history.
        var detail = await Run(e, sp => sp.GetRequiredService<IDebtService>().EntryAsync(entry, Token));
        Assert.Equal(("DX-2026", "TD-2027", new DateOnly(2027, 4, 14), 2), (detail.OriginSeason!.Code, detail.CollectionSeason!.Code, detail.OriginalDueDate, detail.RolloverCount));
    }

    // V5: a debt that is paid, or whose due date is moved, is not reminded on the old schedule, whatever was already sent.
    [CreditDbFact]
    public async Task V5_paid_or_moved_debt_is_not_reminded_on_the_old_schedule()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var due = new DateOnly(2026, 11, 4);
        var moved = await ManualAsync(e, e.FarmerId, 1_000_000m, due);
        var (paidFarmer, paidUser) = await FarmerAsync(e, "Đã trả");
        await ManualAsync(e, paidFarmer, 1_000_000m, due);
        Task Remind() => Run(e, async sp => { await ReminderStartHelper.EnsureAsync(sp.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token); await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(100, Token); return 0; });
        async Task<List<string>> Keys(Guid user)
        {
            using var s = e.Scope();
            return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.UserId == user && n.DeduplicationKey!.StartsWith("debt-reminder:")).Select(n => n.DeduplicationKey!).ToListAsync(Token)).Order().ToList();
        }

        At(e, 2026, 10, 28);                                                           // N2 of the old schedule goes to both
        await Remind();
        var queued = await Keys(paidUser);
        Assert.Single(queued);
        Assert.Equal(2, queued.Count + (await Keys(e.FarmerUserId)).Count);

        At(e, 2026, 10, 30);                                                           // one is paid in full, the other's due date moves
        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 1_000_000m, FarmerProfileId: paidFarmer), Token));
        await ActionAsync(e, moved.Id, "CHANGE_DUE_DATE", null, new DateOnly(2026, 11, 25), "KHO_KHAN");

        At(e, 2026, 11, 4);                                                            // the old due date arrives: no reminder for it
        await Remind();
        At(e, 2026, 11, 18);
        await Remind();
        Assert.Equal(queued, await Keys(paidUser));                                    // the notice already sent stays, nothing new
        var movedKeys = await Keys(e.FarmerUserId);
        Assert.DoesNotContain(movedKeys, k => k.Contains("20261104:N3") || k.Contains("20261104:N4"));
        Assert.Contains($"debt-reminder:{e.FarmerId}:20261125:N2", movedKeys);          // the new schedule starts

        At(e, 2026, 11, 25);
        await Remind();
        Assert.Contains($"debt-reminder:{e.FarmerId}:20261125:N3", await Keys(e.FarmerUserId));
        Assert.Equal(queued, await Keys(paidUser));
    }

    // V6: the total owed reads the same on the entries, the list, the account, the dashboard, the season report and the ledger,
    // with a payment, a reduction, a forgiven debt and a return in the data.
    [CreditDbFact]
    public async Task V6_the_total_owed_agrees_everywhere()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        CropSeasonResponse dx;
        using (var s = e.Scope())
            dx = await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var o1 = await e.FulfillAsync(10, Token, withoutSeason: true);
        var o2 = await e.FulfillAsync(4, Token, withoutSeason: true);
        var o3 = await e.FulfillAsync(3, Token, withoutSeason: true);
        var manual = (await ManualAsync(e, e.FarmerId, 500_000m, new DateOnly(2026, 12, 1))).Id;
        Guid e1, e2, e3;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            (e1, e2, e3) = (await db.DebtEntries.Where(x => x.OrderId == o1.Id).Select(x => x.Id).SingleAsync(Token),
                await db.DebtEntries.Where(x => x.OrderId == o2.Id).Select(x => x.Id).SingleAsync(Token),
                await db.DebtEntries.Where(x => x.OrderId == o3.Id).Select(x => x.Id).SingleAsync(Token));
            // The first two arose in Đông Xuân (what F3.13 records); the report covers them.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET origin_crop_season_id = {dx.Id}, collection_crop_season_id = {dx.Id} WHERE id IN ({e1}, {e2})", Token);

            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == o1.Id, Token);
            var movement = await db.StockMovements.Include(m => m.Items).SingleAsync(m => m.OrderId == order.Id, Token);
            var sales = new SalesReturn(order, "RT-V6", e.User.UserId!.Value, e.Clock.UtcNow);
            sales.AddItem(order, order.Items.Single(), 3, 0, e.LotId, "RETURN", originalStockMovementItemId: movement.Items.Single().Id);
            sales.Approve(e.User.UserId.Value, e.Clock.UtcNow); sales.MarkReceived(e.User.UserId.Value, e.Clock.UtcNow);
            db.SalesReturns.Add(sales); await db.SaveChangesAsync(Token);
            await using var tx = await db.BeginTransactionAsync(Token);
            await s.ServiceProvider.GetRequiredService<IDebtReturnPosting>().ApplyReturnAsync(order.Id, sales.Id, sales.TotalReturnAmount, e.User.UserId.Value, movement.Id, Token);
            await db.SaveChangesAsync(Token); await tx.CommitAsync(Token);
        }

        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 2_000_000m, FarmerProfileId: e.FarmerId), Token));
        await ActionAsync(e, e2, "ADJUST", 1_000_000m, null, "TRI_AN");
        await ActionAsync(e, e3, "CANCEL", null, null, "KHONG_THU_HOI");
        // 10M − 3M returned − 2M paid = 5M; 4M − 1M reduced = 3M; 3M forgiven = 0; 0.5M manual.
        const decimal expected = 8_500_000m;

        var detail = 0m;
        foreach (var id in new[] { e1, e2, e3, manual }) detail += (await Run(e, sp => sp.GetRequiredService<IDebtService>().EntryAsync(id, Token))).OutstandingAmount;
        var list = (await Run(e, sp => sp.GetRequiredService<IDebtService>().EntriesAsync(new() { PageSize = 100 }, Token))).Items.Sum(i => i.OutstandingAmount);
        var account = (await Run(e, sp => sp.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token))).CurrentBalance;
        var dashboard = (await Run(e, sp => sp.GetRequiredService<IDebtService>().DashboardAsync(Token))).TotalOutstandingDebt;
        var groups = (await Run(e, sp => sp.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token))).Groups.Sum(g => g.OutstandingAmount);
        var ledger = (await TotalsAsync(e)).Ledger;
        Assert.Equal(new[] { expected, expected, expected, expected, expected, expected }, new[] { detail, list, account, dashboard, groups, ledger });

        // The season report covers the two entries of Đông Xuân: 5M + 3M, and its own identity holds.
        var report = (await Run(e, sp => sp.GetRequiredService<ICropSeasonClosingService>().ReportAsync(new() { PageSize = 10 }, Token))).Items.Single();
        var o = report.Origin;
        Assert.Equal((8_000_000m, 14_000_000m, 2_000_000m, 3_000_000m, 1_000_000m), (o.OutstandingAmount, o.OriginatedAmount, o.CollectedAmount, o.ReturnedAmount, o.ReducedAmount));
        Assert.Equal(o.OutstandingAmount, o.OriginatedAmount - o.CollectedAmount - o.ReturnedAmount - o.ReducedAmount - o.CancelledAmount);
    }
}

