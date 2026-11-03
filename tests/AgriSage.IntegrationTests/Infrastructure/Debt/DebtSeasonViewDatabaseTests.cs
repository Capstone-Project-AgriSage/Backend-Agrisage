using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.17 against real PostgreSQL: the debt screens by season (D9, D16), due states, "Cần quyết định" and the Farmer's
// groups. The fixture's today is 05/10/2026 (Vietnam); a pickup debt is due on 04/11/2026 (30-day tier term, no season).
public sealed class DebtSeasonViewDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CreateCropSeasonRequest DongXuan =
        new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1));
    private static readonly CreateCropSeasonRequest HeThu =
        new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task<T> Debts<T>(CreditTestDatabase e, Func<IDebtService, Task<T>> act) =>
        Run(e, sp => act(sp.GetRequiredService<IDebtService>()));

    private static Task<CropSeasonResponse> Season(CreditTestDatabase e, CreateCropSeasonRequest request) =>
        Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(request, Token));

    private static async Task<Guid> DebtAsync(CreditTestDatabase e, long quantity = 1)
    {
        var order = await e.FulfillAsync(quantity, Token);
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id, Token)).Id;
    }

    // What F3.13 will record at delivery: the season and its due date.
    private static async Task SetAsync(CreditTestDatabase e, Guid id, DateOnly due, Guid? season = null, int rollovers = 0)
    {
        using var s = e.Scope();
        await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE debt_entries SET due_date = {due}, original_due_date = {due}, origin_crop_season_id = {season}, collection_crop_season_id = {season}, rollover_count = {rollovers} WHERE id = {id}", Token);
    }

    private static async Task<List<Guid>> IdsAsync(CreditTestDatabase e, DebtEntryListRequest request) =>
        (await Debts(e, d => d.EntriesAsync(request with { PageSize = 100 }, Token))).Items.Select(i => i.Id).Order().ToList();

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();

    [CreditDbFact]
    public async Task Entries_show_their_origin_and_collection_season_and_filter_by_them()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var ht = await Season(e, HeThu);
        var rolled = await DebtAsync(e, 2);
        var plain = await DebtAsync(e);
        await SetAsync(e, rolled, new DateOnly(2027, 4, 14), dx.Id);
        await Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(rolled, new("MAT_MUA", "Mất mùa", ht.Id), Token));

        var items = (await Debts(e, d => d.EntriesAsync(new() { PageSize = 100 }, Token))).Items.ToDictionary(i => i.Id);
        var r = items[rolled];
        Assert.Equal(("DX-2026", "HT-2027", new DateOnly(2027, 4, 14), new DateOnly(2027, 9, 14), 1, true, "NOT_DUE", false, 2_000_000m),
            (r.OriginSeason!.Code, r.CollectionSeason!.Code, r.OriginalDueDate, r.DueDate, r.RolloverCount, r.IsRolledOver, r.DueState,
                r.NeedsDecision, r.OutstandingAmount));
        Assert.Equal((dx.Id, "Đông Xuân 2026–2027", ht.Id), (r.OriginSeason.Id, r.OriginSeason.Name, r.CollectionSeason.Id));
        var p = items[plain];
        Assert.Equal(((DebtSeasonReference?)null, (DebtSeasonReference?)null, new DateOnly(2026, 11, 4), 0, false, "NOT_DUE"),
            (p.OriginSeason, p.CollectionSeason, p.OriginalDueDate, p.RolloverCount, p.IsRolledOver, p.DueState));

        Assert.Equal([rolled], await IdsAsync(e, new() { OriginSeasonId = dx.Id }));
        Assert.Empty(await IdsAsync(e, new() { CollectionSeasonId = dx.Id }));
        Assert.Equal([rolled], await IdsAsync(e, new() { CollectionSeasonId = ht.Id }));
        Assert.Equal([rolled], await IdsAsync(e, new() { RolledOverOnly = true }));

        var detail = await Debts(e, d => d.EntryAsync(rolled, Token));
        Assert.Equal(("DX-2026", "HT-2027", new DateOnly(2027, 4, 14), 1, true, "NOT_DUE", 2_000_000m, "OPEN"),
            (detail.OriginSeason!.Code, detail.CollectionSeason!.Code, detail.OriginalDueDate, detail.RolloverCount, detail.IsRolledOver,
                detail.DueState, detail.OutstandingAmount, detail.Status));
        var action = Assert.Single(detail.Actions);
        Assert.Equal(("ROLLOVER", "MAT_MUA", "Mất mùa", "Owner", "STORE_OWNER", dx.Id, ht.Id),
            (action.ActionType, action.ReasonCode, action.Reason, action.ActorName, action.ActorRole, action.OldCropSeasonId!.Value,
                action.NewCropSeasonId!.Value));
    }

    // DUE_SOON = due today or within 7 days; an entry with nothing left to pay has no due state.
    [CreditDbFact]
    public async Task Due_states_follow_the_due_date_boundaries()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var overdue = await DebtAsync(e);
        var today = await DebtAsync(e);
        var inSeven = await DebtAsync(e);
        var inEight = await DebtAsync(e);
        var settled = await DebtAsync(e);
        await SetAsync(e, overdue, new DateOnly(2026, 10, 4));
        await SetAsync(e, today, new DateOnly(2026, 10, 5));
        await SetAsync(e, inSeven, new DateOnly(2026, 10, 12));
        await SetAsync(e, inEight, new DateOnly(2026, 10, 13));
        await SetAsync(e, settled, new DateOnly(2026, 10, 1));
        await Debts(e, d => d.ActionAsync(settled, "CANCEL", "Miễn nợ", null, null, "TRI_AN", Token));

        var states = (await Debts(e, d => d.EntriesAsync(new() { PageSize = 100 }, Token))).Items.ToDictionary(i => i.Id, i => i.DueState);
        Assert.Equal(("OVERDUE", "DUE_SOON", "DUE_SOON", "NOT_DUE", (string?)null),
            (states[overdue], states[today], states[inSeven], states[inEight], states[settled]));
        Assert.Equal([overdue], await IdsAsync(e, new() { DueState = "OVERDUE" }));
        Assert.Equal(Sorted(today, inSeven), await IdsAsync(e, new() { DueState = "DUE_SOON" }));
        Assert.Equal([inEight], await IdsAsync(e, new() { DueState = "NOT_DUE" }));
        Assert.Equal("DUE_SOON", (await Debts(e, d => d.EntryAsync(today, Token))).DueState);
        Assert.Null((await Debts(e, d => d.EntryAsync(settled, Token))).DueState);
    }

    // "Cần quyết định": still owed, not disputed, and either past the last reminder (due + N5 ≤ today) or rolled over as
    // often as the owner wants to review.
    [CreditDbFact]
    public async Task Needs_decision_follows_the_store_debt_policy()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var pastN5 = await DebtAsync(e);
        var dayBeforeN5 = await DebtAsync(e);
        var disputed = await DebtAsync(e);
        var rolledTwice = await DebtAsync(e);
        var cancelled = await DebtAsync(e);
        await SetAsync(e, pastN5, new DateOnly(2026, 9, 5));        // today − 30 = N5 (default 30)
        await SetAsync(e, dayBeforeN5, new DateOnly(2026, 9, 6));   // 29 days overdue
        await SetAsync(e, disputed, new DateOnly(2026, 8, 1));
        await SetAsync(e, rolledTwice, new DateOnly(2026, 12, 1), rollovers: 2); // owner review after 2 (default)
        await SetAsync(e, cancelled, new DateOnly(2026, 8, 1));
        await Debts(e, d => d.ActionAsync(disputed, "DISPUTE", "Sai số lượng", null, null, Token));
        await Debts(e, d => d.ActionAsync(cancelled, "CANCEL", "Miễn nợ", null, null, "KHONG_THU_HOI", Token));

        Assert.Equal(Sorted(pastN5, rolledTwice), await IdsAsync(e, new() { NeedsDecision = true }));
        var flags = (await Debts(e, d => d.EntriesAsync(new() { PageSize = 100 }, Token))).Items.ToDictionary(i => i.Id, i => i.NeedsDecision);
        Assert.Equal((true, false, false, true, false), (flags[pastN5], flags[dayBeforeN5], flags[disputed], flags[rolledTwice], flags[cancelled]));
        var dashboard = await Debts(e, d => d.DashboardAsync(Token));
        Assert.Equal(2, dashboard.NeedsDecisionCount);
        Assert.Equal(Sorted(pastN5, rolledTwice), dashboard.NeedsDecision.Select(i => i.Id).Order().ToList());
        Assert.All(dashboard.NeedsDecision, i => Assert.True(i.NeedsDecision));
        Assert.True((await Debts(e, d => d.EntryAsync(pastN5, Token))).NeedsDecision);

        // A store policy with N5 = 29 and owner review after 3 rollovers changes the list.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(30, new DebtReminderOffsets(-7, 14, 29), 1, 3, 2, null, 365);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(Token);
        }
        Assert.Equal(Sorted(pastN5, dayBeforeN5), await IdsAsync(e, new() { NeedsDecision = true }));
        Assert.Equal(2, (await Debts(e, d => d.DashboardAsync(Token))).NeedsDecisionCount);
    }

    [CreditDbFact]
    public async Task The_dashboard_shows_the_current_season_due_soon_debt_and_customers_near_their_limit()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var ht = await Season(e, HeThu);
        var inDx = await DebtAsync(e, 3);
        var inHt = await DebtAsync(e, 2);
        var soon = await DebtAsync(e);
        await SetAsync(e, inDx, new DateOnly(2027, 4, 14), dx.Id);
        await SetAsync(e, inHt, new DateOnly(2027, 9, 14), ht.Id);
        await SetAsync(e, soon, new DateOnly(2026, 10, 10));

        var dashboard = await Debts(e, d => d.DashboardAsync(Token));
        // The current season is the open one collected next: the earliest due date not yet past.
        Assert.Equal((dx.Id, 3_000_000m, 1_000_000m, 6_000_000m), (dashboard.CurrentSeason!.Id, dashboard.CurrentSeasonOutstanding,
            dashboard.DueSoonAmount, dashboard.TotalOutstandingDebt));
        Assert.Equal(0, dashboard.CreditLimitAlertCount);
        Assert.Empty(dashboard.CreditLimitAlerts);

        // 18 of 20 million used: near the limit (90 %); with 2 million more reserved by a confirmed order: at the limit.
        await DebtAsync(e, 12);
        var near = Assert.Single((await Debts(e, d => d.DashboardAsync(Token))).CreditLimitAlerts);
        Assert.Equal((e.FarmerId, "Farmer Test", 20_000_000m, 18_000_000m, 2_000_000m, "NEAR_LIMIT"),
            (near.FarmerProfileId, near.FullName, near.CreditLimit, near.UsedCredit, near.AvailableCredit, near.State));
        var order = await e.OrderAsync(2, Token);
        await Run(e, sp => sp.GetRequiredService<AgriSage.Application.Features.Orders.IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        var at = await Debts(e, d => d.DashboardAsync(Token));
        Assert.Equal((1, 20_000_000m, 0m, "AT_LIMIT"),
            (at.CreditLimitAlertCount, at.CreditLimitAlerts[0].UsedCredit, at.CreditLimitAlerts[0].AvailableCredit, at.CreditLimitAlerts[0].State));
    }

    [CreditDbFact]
    public async Task Without_open_seasons_the_dashboard_has_no_current_season()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await DebtAsync(e);
        var dashboard = await Debts(e, d => d.DashboardAsync(Token));
        Assert.Equal(((DebtSeasonReference?)null, 0m), (dashboard.CurrentSeason, dashboard.CurrentSeasonOutstanding));
    }

    // GET /api/me/debt groups the Farmer's open debt by (collection season, due date), as the reminders do (D10). The
    // Farmer routes force the caller's own customer: another Farmer's entries never appear.
    [CreditDbFact]
    public async Task The_farmer_sees_own_debt_grouped_by_season_and_due_date_and_never_another_farmers()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var a = await DebtAsync(e, 2);
        var b = await DebtAsync(e, 3);
        var c = await DebtAsync(e);
        var paid = await DebtAsync(e);
        await SetAsync(e, a, new DateOnly(2027, 4, 14), dx.Id);
        await SetAsync(e, b, new DateOnly(2027, 4, 14), dx.Id);
        await Debts(e, d => d.ActionAsync(paid, "CANCEL", "Miễn nợ", null, null, Token));

        Guid otherEntry;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
            var user = new User(role.Id, "Other farmer", "hash", "other@example.test", "0987654321");
            var farmer = new FarmerProfile(user.Id);
            var account = new DebtAccount(e.StoreId, farmer.Id);
            var posting = account.CreateManualAdjustmentEntry("DE-OTHER", 7_000_000m, new DateOnly(2027, 4, 14), e.User.UserId!.Value, e.Clock.UtcNow);
            db.AddRange(user, farmer, account, posting.Entry, posting.Transaction);
            await db.SaveChangesAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE debt_entries SET origin_crop_season_id = {dx.Id}, collection_crop_season_id = {dx.Id} WHERE id = {posting.Entry.Id}", Token);
            otherEntry = posting.Entry.Id;
        }

        var account1 = await Debts(e, d => d.AccountAsync(e.FarmerId, Token));
        Assert.Equal(6_000_000m, account1.CurrentBalance);
        Assert.Collection(account1.Groups,
            g => Assert.Equal(((Guid?)null, new DateOnly(2026, 11, 4), 1_000_000m, 1, "NOT_DUE"),
                (g.CollectionSeason?.Id, g.DueDate, g.OutstandingAmount, g.EntryCount, g.DueState)),
            g => Assert.Equal(((Guid?)dx.Id, new DateOnly(2027, 4, 14), 5_000_000m, 2, "NOT_DUE"),
                (g.CollectionSeason?.Id, g.DueDate, g.OutstandingAmount, g.EntryCount, g.DueState)));

        // The Farmer routes pass the caller's customer; new filters never widen it.
        var own = await IdsAsync(e, new() { FarmerProfileId = e.FarmerId, CollectionSeasonId = dx.Id });
        Assert.Equal(Sorted(a, b), own);
        Assert.DoesNotContain(otherEntry, await IdsAsync(e, new() { FarmerProfileId = e.FarmerId, OriginSeasonId = dx.Id }));
        using (var s = e.Scope())
        {
            var debts = s.ServiceProvider.GetRequiredService<IDebtService>();
            await Assert.ThrowsAsync<NotFoundException>(() => debts.RequireOwnedEntryAsync(otherEntry, e.FarmerId, Token));
        }

        // Staff see both customers' debt of the season.
        Assert.Equal(Sorted(a, b, otherEntry), await IdsAsync(e, new() { CollectionSeasonId = dx.Id }));
    }
}
