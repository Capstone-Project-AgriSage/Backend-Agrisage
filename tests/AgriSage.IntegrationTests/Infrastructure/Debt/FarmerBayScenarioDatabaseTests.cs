using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20: the golden scenario "nông dân Bảy" (docs/agent/requests/DEBT_BY_CROP_SEASON_TASKS.md §8.1), run end to end through the
// real use cases with a fake clock on real PostgreSQL. The fixture's price is 1,000,000 per unit, so the document's amounts are
// scaled: A 3,000,000 (06/12/2026), B 2,000,000 (11/01/2027), C 1,000,000 (20/02/2027), payment 2,000,000 (25/03/2027).
// Seasons: Đông Xuân orders 01/10/2026–14/02/2027, harvest 15/02–15/03/2027, due 14/04/2027; Hè Thu orders from 15/02/2027, due
// 14/09/2027; Thu Đông orders from 15/07/2027, harvest 15/11–15/12/2027, due 14/01/2028.
public sealed class FarmerBayScenarioDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateOnly DxDue = new(2027, 4, 14);
    private static readonly DateOnly HtDue = new(2027, 9, 14);
    private static readonly DateOnly TdDue = new(2028, 1, 14);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<OrderResponse> DeliverAsync(CreditTestDatabase e, long quantity)
    {
        var order = await e.OrderAsync(quantity, Token);
        await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        return await Run(e, sp => sp.GetRequiredService<IOrderPickupService>().PickupAsync(order.Id,
            new([new(order.Items.Single().Id, [new(e.LotId, quantity)])]), Token));
    }

    private static Task RemindAsync(CreditTestDatabase e) =>
        Run(e, async sp => { await ReminderStartHelper.EnsureAsync(sp.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token); await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(100, Token); return 0; });

    private static async Task<List<string>> ReminderKeysAsync(CreditTestDatabase e, Guid user)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.UserId == user && n.DeduplicationKey!.StartsWith("debt-reminder")).Select(n => n.DeduplicationKey!).ToListAsync(Token))
            .Order().ToList();
    }

    private static Task<DebtEntryResponse> EntryAsync(CreditTestDatabase e, Guid id) =>
        Run(e, sp => sp.GetRequiredService<IDebtService>().EntryAsync(id, Token));

    private static async Task<(int Ledger, decimal Balance)> LedgerAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return (await db.DebtTransactions.CountAsync(Token), (await db.DebtAccounts.AsNoTracking().SingleAsync(x => x.FarmerProfileId == e.FarmerId, Token)).CurrentBalance);
    }

    private static async Task<Guid> EntryOfAsync(CreditTestDatabase e, Guid orderId)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == orderId, Token)).Id;
    }

    private static async Task As(CreditTestDatabase e, Guid user, string role, Func<Task> act)
    {
        var (oldUser, oldRole) = (e.User.UserId, e.User.Role);
        e.User.UserId = user; e.User.Role = role;
        try { await act(); }
        finally { e.User.UserId = oldUser; e.User.Role = oldRole; }
    }

    [CreditDbFact]
    public async Task Farmer_Bay_from_the_first_order_to_closing_Dong_Xuan()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var owner = e.User.UserId!.Value;
        var sales = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            db.StoreMemberPermissions.Add(new StoreMemberPermission(sales.MemberId, PermissionCatalog.ByCode["DEBT.ROLLOVER"].Id, true));
            await db.SaveChangesAsync(Token);
        }

        CropSeasonResponse dx, ht, td;
        using (var s = e.Scope())
        {
            var seasons = s.ServiceProvider.GetRequiredService<ICropSeasonService>();
            dx = await seasons.CreateAsync(new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
            ht = await seasons.CreateAsync(new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token);
            td = await seasons.CreateAsync(new("TD-2027", "Thu Đông 2027", new DateOnly(2027, 11, 15), new DateOnly(2027, 12, 15), 30), Token);
        }

        // 06/12/2026: a credit order confirmed and delivered -> debt A, season Đông Xuân, due 14/04/2027.
        At(e, 2026, 12, 6);
        var orderA = await DeliverAsync(e, 3);
        var a = await EntryOfAsync(e, orderA.Id);
        var entryA = await EntryAsync(e, a);
        Assert.Equal(("DX-2026", "DX-2026", DxDue, DxDue, 3_000_000m, "NOT_DUE"),
            (entryA.OriginSeason!.Code, entryA.CollectionSeason!.Code, entryA.DueDate, entryA.OriginalDueDate, entryA.OutstandingAmount, entryA.DueState));

        // 11/01/2027: another order -> debt B, same season, same due date.
        At(e, 2027, 1, 11);
        var b = await EntryOfAsync(e, (await DeliverAsync(e, 2)).Id);
        Assert.Equal((DxDue, "DX-2026"), ((await EntryAsync(e, b)).DueDate, (await EntryAsync(e, b)).OriginSeason!.Code));

        // 15/02/2027, the harvest starts: N1 for Đông Xuân with the 5,000,000 owed.
        At(e, 2027, 2, 15);
        await RemindAsync(e);
        await RemindAsync(e);
        Assert.Equal([$"debt-reminder:{e.FarmerId}:{dx.Id}:N1"], await ReminderKeysAsync(e, e.FarmerUserId));
        using (var s = e.Scope())
        {
            var message = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.UserId == e.FarmerUserId).Select(n => n.Message).SingleAsync(Token);
            Assert.Contains("5.000.000 đ", message);
            Assert.Contains("14/04/2027", message);
        }

        // 20/02/2027, confirmed while the harvest is on: debt C belongs to Hè Thu, due 14/09/2027; N1 of Đông Xuân does not count it.
        At(e, 2027, 2, 20);
        var orderC = await e.OrderAsync(1, Token);
        var confirmedC = await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(orderC.Id, Token));
        Assert.Equal(("HT-2027", HtDue), (confirmedC.OriginSeason!.Code, confirmedC.ExpectedDueDate));
        await Run(e, sp => sp.GetRequiredService<IOrderPickupService>().PickupAsync(orderC.Id, new([new(orderC.Items.Single().Id, [new(e.LotId, 1)])]), Token));
        var c = await EntryOfAsync(e, orderC.Id);
        await RemindAsync(e);
        Assert.Single(await ReminderKeysAsync(e, e.FarmerUserId));

        // 25/03/2027: a payment of 2,000,000 settles the oldest first: A keeps 1,000,000, B 2,000,000.
        At(e, 2027, 3, 25);
        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 2_000_000m, FarmerProfileId: e.FarmerId), Token));
        Assert.Equal((1_000_000m, 2_000_000m, 1_000_000m), ((await EntryAsync(e, a)).OutstandingAmount, (await EntryAsync(e, b)).OutstandingAmount,
            (await EntryAsync(e, c)).OutstandingAmount));

        // 07/04: N2 to the Farmer; 14/04: N3 to the Farmer and one summary to the owner and to Sale staff; not overdue yet.
        At(e, 2027, 4, 7);
        await RemindAsync(e);
        At(e, 2027, 4, 14);
        await RemindAsync(e);
        await RemindAsync(e);
        var farmerKeys = await ReminderKeysAsync(e, e.FarmerUserId);
        Assert.Contains($"debt-reminder:{e.FarmerId}:20270414:N2", farmerKeys);
        Assert.Contains($"debt-reminder:{e.FarmerId}:20270414:N3", farmerKeys);
        Assert.Contains($"debt-reminder-staff:{e.StoreId:N}:20270414:N3", await ReminderKeysAsync(e, owner));
        Assert.Contains($"debt-reminder-staff:{e.StoreId:N}:20270414:N3", await ReminderKeysAsync(e, sales.UserId));
        Assert.Equal("DUE_SOON", (await EntryAsync(e, a)).DueState);

        // 15/04/2027: one day overdue, and the Farmer can still buy on credit within the remaining limit (CS-10).
        At(e, 2027, 4, 15);
        var overdueEntry = await EntryAsync(e, a);
        Assert.Equal(("OVERDUE", 1, true), (overdueEntry.DueState, overdueEntry.OverdueDays, overdueEntry.IsOverdue));
        var check = await Run(e, sp => sp.GetRequiredService<ICreditEligibilityService>().CheckAsync(e.FarmerId, 1_000_000m, Token));
        Assert.Equal((true, true, "HT-2027"), (check.Eligible, check.HasOverdueDebt, check.ExpectedCropSeason!.Code));
        var orderD = await e.OrderAsync(1, Token);
        Assert.Equal("HT-2027", (await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(orderD.Id, Token))).OriginSeason!.Code);

        // 28/04/2027: N4 goes out for the old due date, then Sale staff roll A and B over to Hè Thu.
        At(e, 2027, 4, 28);
        await RemindAsync(e);
        Assert.Contains($"debt-reminder:{e.FarmerId}:20270414:N4", await ReminderKeysAsync(e, e.FarmerUserId));
        var before = await LedgerAsync(e);
        await As(e, sales.UserId, "SALES_STAFF", async () =>
        {
            foreach (var id in new[] { a, b })
            {
                var rolled = await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(id, new("MAT_MUA", "Mất mùa do sâu cuốn lá"), Token));
                Assert.Equal(("HT-2027", "DX-2026", HtDue, DxDue, 1), (rolled.CollectionSeason!.Code, rolled.OriginSeason!.Code, rolled.DueDate,
                    rolled.OriginalDueDate, rolled.RolloverCount));
            }
        });
        // Nothing but the season and the due date moved: same amounts, no new ledger line.
        Assert.Equal(before, await LedgerAsync(e));
        Assert.Equal((1_000_000m, 2_000_000m), ((await EntryAsync(e, a)).OutstandingAmount, (await EntryAsync(e, b)).OutstandingAmount));

        // The old schedule is over: no more reminders for 14/04, and the second rollover is the owner's decision.
        var reminders = (await ReminderKeysAsync(e, e.FarmerUserId)).Count;
        At(e, 2027, 5, 14);
        await RemindAsync(e);
        Assert.Equal(reminders, (await ReminderKeysAsync(e, e.FarmerUserId)).Count);
        await As(e, sales.UserId, "SALES_STAFF", () => Assert.ThrowsAsync<ForbiddenException>(() =>
            Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(a, new("MAT_MUA", "Lần hai", td.Id), Token))));
        Assert.Equal(1, (await EntryAsync(e, a)).RolloverCount);
        var second = await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(a, new("THIEN_TAI", "Lũ lớn cuối vụ", td.Id), Token));
        Assert.Equal(("TD-2027", "DX-2026", TdDue, DxDue, 2, true), (second.CollectionSeason!.Code, second.OriginSeason!.Code, second.DueDate,
            second.OriginalDueDate, second.RolloverCount, second.NeedsDecision));
        Assert.Equal(["ROLLOVER", "ROLLOVER"], second.Actions.Select(x => x.ActionType));
        Assert.Equal((DxDue, HtDue, HtDue, TdDue), (second.Actions[0].OldDueDate!.Value, second.Actions[0].NewDueDate!.Value,
            second.Actions[1].OldDueDate!.Value, second.Actions[1].NewDueDate!.Value));

        // Farmer's view: the open debt is grouped by where and when it is collected.
        var account = await Run(e, sp => sp.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token));
        Assert.Equal(4_000_000m, account.CurrentBalance);
        Assert.Equal(new[] { (HtDue, 3_000_000m, 2), (TdDue, 1_000_000m, 1) }, account.Groups.Select(g => (g.DueDate, g.OutstandingAmount, g.EntryCount)));

        // Đông Xuân is collecting and nothing is collected in it any more: the owner closes it.
        var check2 = await Run(e, sp => sp.GetRequiredService<ICropSeasonClosingService>().ClosingCheckAsync(dx.Id, Token));
        Assert.Equal((true, "COLLECTING", 0, 0), (check2.CanClose, check2.Status, check2.OpenEntryCount, check2.OpenCreditOrderCount));
        var current = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().GetAsync(dx.Id, Token));
        var closed = await Run(e, sp => sp.GetRequiredService<ICropSeasonClosingService>().CloseAsync(dx.Id, new(current.Version, "Đã chuyển hết sang Hè Thu"), Token));
        Assert.Equal("CLOSED", closed.Status);

        // Report by origin season: Đông Xuân 5,000,000 originated, 2,000,000 collected, 3,000,000 carried over (A and B). Later
        // payments land in the seasons that collect the debt and never change the closed season's figures.
        await Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 2_000_000m, FarmerProfileId: e.FarmerId), Token));
        var report = (await Run(e, sp => sp.GetRequiredService<ICropSeasonClosingService>().ReportAsync(new() { PageSize = 10 }, Token))).Items;
        var dxReport = report.Single(r => r.Season.Id == dx.Id);
        Assert.True(dxReport.IsFrozen);
        Assert.Equal(new SeasonOriginFigures(5_000_000m, 2_000_000m, 0m, 0m, 0m, 3_000_000m, 3_000_000m, 2, 1), dxReport.Origin);
        Assert.Equal(new SeasonCollectionFigures(0m, 0m, 0), dxReport.Collection);
        // The 2,000,000 settled B (oldest due date, then oldest entry). Hè Thu: C (1,000,000) arose there and is still owed; B was
        // carried in and is paid, A carried to Thu Đông.
        var htReport = report.Single(r => r.Season.Id == ht.Id);
        Assert.False(htReport.IsFrozen);
        Assert.Equal(new SeasonOriginFigures(1_000_000m, 0m, 0m, 0m, 0m, 0m, 1_000_000m, 1, 1), htReport.Origin);
        Assert.Equal(new SeasonCollectionFigures(0m, 1_000_000m, 1), htReport.Collection);
        var tdReport = report.Single(r => r.Season.Id == td.Id);
        Assert.Equal(new SeasonCollectionFigures(1_000_000m, 1_000_000m, 1), tdReport.Collection);
        Assert.Equal(2_000_000m, (await Run(e, sp => sp.GetRequiredService<IDebtService>().AccountAsync(e.FarmerId, Token))).CurrentBalance);
        Assert.Equal(0m, (await EntryAsync(e, b)).OutstandingAmount);
    }
}
