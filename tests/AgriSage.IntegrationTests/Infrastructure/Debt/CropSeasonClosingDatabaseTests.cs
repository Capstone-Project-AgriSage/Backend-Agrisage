using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.19 against real PostgreSQL: closing a season (D11), the debt-by-season report (D9) and limit suggestions (D12, CS-14).
// Đông Xuân: harvest 15/02–15/03/2027, due 14/04/2027. Hè Thu: harvest 15/07–15/08/2027, due 14/09/2027.
public sealed class CropSeasonClosingDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateOnly DxDue = new(2027, 4, 14);
    private static readonly DateOnly HtDue = new(2027, 9, 14);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task<T> Closing<T>(CreditTestDatabase e, Func<ICropSeasonClosingService, Task<T>> act) =>
        Run(e, sp => act(sp.GetRequiredService<ICropSeasonClosingService>()));

    // Vietnam noon of the day: the business date is that day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<(CropSeasonResponse Dx, CropSeasonResponse Ht)> SeasonsAsync(CreditTestDatabase e)
    {
        using var scope = e.Scope();
        var seasons = scope.ServiceProvider.GetRequiredService<ICropSeasonService>();
        var dx = await seasons.CreateAsync(new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token);
        var ht = await seasons.CreateAsync(new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token);
        return (dx, ht);
    }

    // A debt of the season (what F3.13 will record at delivery), entered as a manual debt for exact amounts.
    private static async Task<Guid> DebtAsync(CreditTestDatabase e, Guid farmer, decimal amount, Guid? season, DateOnly due)
    {
        var entry = await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(amount, due, "Đơn chịu"), Token));
        using var s = e.Scope();
        await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE debt_entries SET origin_crop_season_id = {season}, collection_crop_season_id = {season}, original_due_date = due_date WHERE id = {entry.Id}", Token);
        return entry.Id;
    }

    private static Task PayAsync(CreditTestDatabase e, Guid farmer, decimal amount) =>
        Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", amount, FarmerProfileId: farmer), Token));

    private static Task<DebtEntryResponse> RollAsync(CreditTestDatabase e, Guid entry, Guid? target = null) =>
        Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entry, new("MAT_MUA", "Mất mùa", target), Token));

    private static Task<CropSeasonResponse> CloseAsync(CreditTestDatabase e, Guid season) =>
        Run(e, async sp =>
        {
            var current = await sp.GetRequiredService<ICropSeasonService>().GetAsync(season, Token);
            return await sp.GetRequiredService<ICropSeasonClosingService>().CloseAsync(season, new(current.Version, "Đã thu xong"), Token);
        });

    private static async Task<DebtBySeasonItem> ReportAsync(CreditTestDatabase e, Guid season) =>
        (await Closing(e, c => c.ReportAsync(new() { PageSize = 100 }, Token))).Items.Single(i => i.Season.Id == season);

    private static async Task<Guid> FarmerAsync(CreditTestDatabase e, string name)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
        var user = new User(role.Id, name, "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        db.AddRange(user, farmer, new FarmerCreditProfile(e.StoreId, farmer.Id, 20_000_000m, e.User.UserId!.Value, e.Clock.UtcNow, e.TierId),
            new DebtAccount(e.StoreId, farmer.Id));
        await db.SaveChangesAsync(Token);
        return farmer.Id;
    }

    // §8.1 "nông dân Bảy": two credit debts in Đông Xuân, a 1 million payment, the rest rolled over to Hè Thu, then the
    // owner closes Đông Xuân. The report keeps the rolled-over debt under Đông Xuân (origin) and shows it to collect in Hè Thu.
    [CreditDbFact]
    public async Task Farmer_Bay_closes_Dong_Xuan_and_the_report_follows_the_origin_season()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);
        At(e, 2026, 12, 6);
        var a = await DebtAsync(e, e.FarmerId, 1_200_000m, dx.Id, DxDue);
        At(e, 2027, 1, 11);
        var b = await DebtAsync(e, e.FarmerId, 800_000m, dx.Id, DxDue);
        At(e, 2027, 2, 20);
        var c = await DebtAsync(e, e.FarmerId, 500_000m, ht.Id, HtDue);
        At(e, 2027, 3, 25);
        await PayAsync(e, e.FarmerId, 1_000_000m);
        At(e, 2027, 4, 28);

        var blocked = await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token));
        Assert.Equal((false, 2, 0, "COLLECTING"), (blocked.CanClose, blocked.OpenEntryCount, blocked.OpenCreditOrderCount, blocked.Status));
        Assert.Equal(["OPEN_ENTRIES"], blocked.Reasons);
        Assert.Equal(new[] { a, b }.Order(), blocked.OpenEntries.Select(o => o.EntryId).Order());
        var openA = blocked.OpenEntries.Single(o => o.EntryId == a);
        Assert.Equal((200_000m, "Farmer Test", e.FarmerId, "PARTIALLY_PAID"), (openA.OutstandingAmount, openA.FullName, openA.FarmerProfileId, openA.Status));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CloseAsync(e, dx.Id));

        await RollAsync(e, a);
        await RollAsync(e, b);
        Assert.True((await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token))).CanClose);
        var closed = await CloseAsync(e, dx.Id);
        Assert.Equal("CLOSED", closed.Status);

        var dxReport = await ReportAsync(e, dx.Id);
        Assert.True(dxReport.IsFrozen);
        Assert.Equal(new SeasonOriginFigures(2_000_000m, 1_000_000m, 0m, 0m, 0m, 1_000_000m, 1_000_000m, 2, 1), dxReport.Origin);
        Assert.Equal(new SeasonCollectionFigures(0m, 0m, 0), dxReport.Collection);
        var htReport = await ReportAsync(e, ht.Id);
        Assert.False(htReport.IsFrozen);
        Assert.Equal(new SeasonOriginFigures(500_000m, 0m, 0m, 0m, 0m, 0m, 500_000m, 1, 1), htReport.Origin);
        Assert.Equal(new SeasonCollectionFigures(1_000_000m, 1_500_000m, 3), htReport.Collection);

        // After closing, Đông Xuân's figures are frozen; Hè Thu's follow the ledger.
        await PayAsync(e, e.FarmerId, 300_000m);
        Assert.Equal(1_000_000m, (await ReportAsync(e, dx.Id)).Origin.CollectedAmount);
        Assert.Equal(1_200_000m, (await ReportAsync(e, ht.Id)).Collection.ToCollectAmount);

        // A closed season never changes and debt cannot move into it.
        await Assert.ThrowsAsync<BusinessRuleException>(() => CloseAsync(e, dx.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, sp => sp.GetRequiredService<ICropSeasonService>()
            .UpdateAsync(dx.Id, new("X", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, closed.Version), Token)));
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var season = await db.CropSeasons.AsNoTracking().SingleAsync(x => x.Id == dx.Id, Token);
            Assert.Equal(e.User.UserId, season.ClosedBy);
            Assert.NotNull(season.ClosedSummary);
            var audit = await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == "CROP_SEASON_CLOSED", Token);
            Assert.Equal(("CROP_SEASON", dx.Id, "Đã thu xong"), (audit.EntityType, audit.EntityId!.Value, audit.Reason));
        }
    }

    // D11: still-owed debt (disputed included), an unfinished credit order of the season, or a harvest not yet ended.
    [CreditDbFact]
    public async Task Closing_is_blocked_by_open_debt_open_credit_orders_and_an_unfinished_harvest()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, _) = await SeasonsAsync(e);
        var disputed = await DebtAsync(e, e.FarmerId, 300_000m, dx.Id, DxDue);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "DISPUTE", "Sai số lượng", null, null, Token));

        var early = await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token));
        Assert.Equal(["HARVEST_NOT_ENDED", "OPEN_ENTRIES"], early.Reasons);
        Assert.Equal("DISPUTED", Assert.Single(early.OpenEntries).Status);

        var order = await e.OrderAsync(1, Token);
        await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        using (var s = e.Scope())
        {
            await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE orders SET crop_season_id = {dx.Id} WHERE id = {order.Id}", Token);
        }

        At(e, 2027, 3, 15); // the last harvest day is still HARVESTING
        Assert.Contains("HARVEST_NOT_ENDED", (await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token))).Reasons);
        At(e, 2027, 3, 16);
        var check = await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token));
        Assert.Equal(["OPEN_ENTRIES", "OPEN_CREDIT_ORDERS"], check.Reasons);
        var open = Assert.Single(check.OpenCreditOrders);
        Assert.Equal((order.Id, order.OrderNumber, "CONFIRMED"), (open.OrderId, open.OrderNumber, open.Status));
        await Assert.ThrowsAsync<BusinessRuleException>(() => CloseAsync(e, dx.Id));

        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "CANCEL", "Miễn nợ", null, null, "TRI_AN", Token));
        Assert.Equal(["OPEN_CREDIT_ORDERS"], (await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token))).Reasons);
        await Run(e, sp => sp.GetRequiredService<IOrderCancellationService>().CancelAsync(order.Id, new("Khách đổi ý"), Token));
        Assert.True((await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token))).CanClose);
        await CloseAsync(e, dx.Id);
        var after = await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token));
        Assert.Equal((false, "CLOSED"), (after.CanClose, after.Status));
        Assert.Equal(["ALREADY_CLOSED"], after.Reasons);
    }

    [CreditDbFact]
    public async Task Only_the_owner_closes_and_a_stale_version_is_refused()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, _) = await SeasonsAsync(e);
        At(e, 2027, 4, 28);
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        var owner = e.User.UserId;
        e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";
        try
        {
            Assert.True((await Closing(e, x => x.ClosingCheckAsync(dx.Id, Token))).CanClose);
            await Assert.ThrowsAsync<ForbiddenException>(() => Closing(e, x => x.CloseAsync(dx.Id, new(dx.Version), Token)));
        }
        finally { e.User.UserId = owner; e.User.Role = "STORE_OWNER"; }

        await Assert.ThrowsAsync<ConflictException>(() => Closing(e, x => x.CloseAsync(dx.Id, new(dx.Version + 1), Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => Closing(e, x => x.CloseAsync(Guid.NewGuid(), new(0), Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => Closing(e, x => x.ClosingCheckAsync(Guid.NewGuid(), Token)));
        Assert.Equal("CLOSED", (await Closing(e, x => x.CloseAsync(dx.Id, new(dx.Version), Token))).Status);
        // Closing changed the version: the old one is now stale for any other change.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Closing(e, x => x.CloseAsync(dx.Id, new(dx.Version + 1), Token)));
    }

    // While a closing holds the store lock (not yet committed), a rollover into that season waits for it, then sees the
    // season closed and is refused: debt never moves into a season that is being closed.
    [CreditDbFact]
    public async Task A_rollover_into_a_season_being_closed_waits_and_is_refused()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, _) = await SeasonsAsync(e);
        var single = await DebtAsync(e, e.FarmerId, 400_000m, null, new DateOnly(2027, 4, 1));
        var listed = await DebtAsync(e, e.FarmerId, 300_000m, null, new DateOnly(2027, 4, 2));
        At(e, 2027, 4, 28);

        using var closer = e.Scope();
        var db = closer.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await closer.ServiceProvider.GetRequiredService<AgriSage.Application.Common.Interfaces.IRowLockService>().LockStoreAsync(e.StoreId, Token);
        var season = await db.CropSeasons.SingleAsync(x => x.Id == dx.Id, Token);
        season.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 4, 28));
        await db.SaveChangesAsync(Token);

        var roll = RollAsync(e, single, dx.Id);
        // The other rollover path too (a listed entry, collected in no season, is not in Đông Xuân; it only has to wait).
        var other = Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(listed, new("MAT_MUA", "Mất mùa", dx.Id), Token));
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(roll.IsCompleted);
        Assert.False(other.IsCompleted);
        await tx.CommitAsync(Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => roll);
        await Assert.ThrowsAsync<BusinessRuleException>(() => other);
        using var s = e.Scope();
        var entries = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .Where(x => x.Id == single || x.Id == listed).ToListAsync(Token);
        Assert.All(entries, x => Assert.Null(x.CollectionCropSeasonId));
    }

    // The season-wide rollover locks the store too: a rollover into a season being closed waits, then is refused.
    [CreditDbFact]
    public async Task A_season_rollover_into_a_season_being_closed_waits_and_is_refused()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);
        var entry = await DebtAsync(e, e.FarmerId, 400_000m, dx.Id, DxDue);
        At(e, 2027, 9, 20); // Hè Thu is collecting and can be closed; Đông Xuân still holds the entry

        using var closer = e.Scope();
        var db = closer.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await closer.ServiceProvider.GetRequiredService<AgriSage.Application.Common.Interfaces.IRowLockService>().LockStoreAsync(e.StoreId, Token);
        var season = await db.CropSeasons.SingleAsync(x => x.Id == ht.Id, Token);
        season.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 9, 20));
        await db.SaveChangesAsync(Token);

        var roll = Run(e, sp => sp.GetRequiredService<IDebtRolloverService>()
            .RolloverSeasonAsync(dx.Id, new("MAT_MUA", "Mất mùa", ht.Id), Token));
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(roll.IsCompleted);
        await tx.CommitAsync(Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => roll);
        using var s = e.Scope();
        Assert.Equal(dx.Id, (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .SingleAsync(x => x.Id == entry, Token)).CollectionCropSeasonId);
    }

    // Reductions, forgiveness and rollovers keep the report equal to the ledger:
    // originated − collected − returned − reduced − cancelled = outstanding.
    [CreditDbFact]
    public async Task The_report_matches_the_ledger()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);
        var x1 = await DebtAsync(e, e.FarmerId, 2_000_000m, dx.Id, DxDue);
        var x2 = await DebtAsync(e, e.FarmerId, 1_000_000m, dx.Id, DxDue);
        var x3 = await DebtAsync(e, e.FarmerId, 1_000_000m, dx.Id, DxDue);
        var other = await FarmerAsync(e, "Năm");
        await DebtAsync(e, other, 700_000m, dx.Id, DxDue);
        await DebtAsync(e, e.FarmerId, 500_000m, ht.Id, HtDue);
        await DebtAsync(e, e.FarmerId, 250_000m, null, new DateOnly(2027, 12, 1)); // no season, due last: payments skip it
        await PayAsync(e, e.FarmerId, 500_000m);                                     // x1
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(x1, "ADJUST", "Tri ân", 300_000m, null, "TRI_AN", Token));
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(x2, "CANCEL", "Không thu hồi", null, null, "KHONG_THU_HOI", Token));
        await RollAsync(e, x3, ht.Id);

        var report = await ReportAsync(e, dx.Id);
        Assert.Equal(new SeasonOriginFigures(4_700_000m, 500_000m, 0m, 300_000m, 1_000_000m, 1_000_000m, 2_900_000m, 4, 2), report.Origin);
        var o = report.Origin;
        Assert.Equal(o.OutstandingAmount, o.OriginatedAmount - o.CollectedAmount - o.ReturnedAmount - o.ReducedAmount - o.CancelledAmount);
        Assert.Equal(new SeasonCollectionFigures(0m, 1_900_000m, 2), report.Collection);
        Assert.Equal(new SeasonCollectionFigures(1_000_000m, 1_500_000m, 2), (await ReportAsync(e, ht.Id)).Collection);

        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var outstanding = await db.DebtEntries.AsNoTracking().Where(x => x.OriginCropSeasonId == dx.Id).SumAsync(x => x.OutstandingAmount, Token);
        var paid = await (from t in db.DebtTransactions.AsNoTracking()
                          join x in db.DebtEntries.AsNoTracking() on t.DebtEntryId equals x.Id
                          where x.OriginCropSeasonId == dx.Id && t.TransactionType == DebtTransactionType.Payment
                          select -t.AmountDelta).SumAsync(Token);
        Assert.Equal((outstanding, paid), (o.OutstandingAmount, o.CollectedAmount));
        var page = await Closing(e, x => x.ReportAsync(new() { Year = 2027, PageSize = 1 }, Token));
        Assert.Equal((2L, "HT-2027"), (page.TotalCount, Assert.Single(page.Items).Season.Code));
    }

    // D12 / CS-14: every debt of the customer that arose in each of the last N closed seasons (policy, default 2) was paid by
    // real payments on or before its original due date, never rolled over, moved, reduced or forgiven.
    [CreditDbFact]
    public async Task Limit_suggestions_follow_consecutive_on_time_seasons()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);
        var onTime = e.FarmerId;
        var late = await FarmerAsync(e, "Trễ hạn");
        var reduced = await FarmerAsync(e, "Được giảm");
        var rolled = await FarmerAsync(e, "Gia hạn");
        var onlyHt = await FarmerAsync(e, "Chỉ Hè Thu");

        At(e, 2026, 12, 1);
        foreach (var f in new[] { onTime, late, reduced }) await DebtAsync(e, f, 1_000_000m, dx.Id, DxDue);
        var rolledDx = await DebtAsync(e, rolled, 1_000_000m, dx.Id, DxDue);
        At(e, 2027, 4, 14);                                                          // on the due date: on time
        foreach (var f in new[] { onTime, late, reduced }) await PayAsync(e, f, 1_000_000m);
        At(e, 2027, 4, 20);
        await RollAsync(e, rolledDx, ht.Id);
        At(e, 2027, 4, 28);
        await CloseAsync(e, dx.Id);

        At(e, 2027, 5, 1);
        foreach (var f in new[] { onTime, late, onlyHt }) await DebtAsync(e, f, 2_000_000m, ht.Id, HtDue);
        var reducedHt = await DebtAsync(e, reduced, 2_000_000m, ht.Id, HtDue);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(reducedHt, "ADJUST", "Tri ân", 100_000m, null, "TRI_AN", Token));
        At(e, 2027, 9, 10);
        foreach (var f in new[] { onTime, onlyHt }) await PayAsync(e, f, 2_000_000m);
        await PayAsync(e, reduced, 1_900_000m);
        await PayAsync(e, rolled, 1_000_000m);
        At(e, 2027, 9, 15);                                                          // one day late
        await PayAsync(e, late, 2_000_000m);

        // An open season gives no suggestion; Đông Xuân alone is not two seasons yet.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Closing(e, x => x.LimitSuggestionsAsync(ht.Id, Token)));
        var first = await Closing(e, x => x.LimitSuggestionsAsync(dx.Id, Token));
        Assert.Equal((2, 0), (first.RequiredSeasons, first.Items.Count));
        Assert.Empty(first.Seasons);

        At(e, 2027, 9, 30);
        await CloseAsync(e, ht.Id);
        var suggestions = await Closing(e, x => x.LimitSuggestionsAsync(ht.Id, Token));
        Assert.Equal(["DX-2026", "HT-2027"], suggestions.Seasons.Select(s => s.Code).ToList());
        var item = Assert.Single(suggestions.Items);
        Assert.Equal((onTime, "Farmer Test", 20_000_000m, "ACTIVE", 3_000_000m, new DateOnly(2027, 9, 10)),
            (item.FarmerProfileId, item.FullName, item.CreditLimit, item.CreditStatus, item.PaidAmount, item.LastPaidOn));

        // With one season required, Đông Xuân alone qualifies the customers who paid it on time.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(30, DebtReminderOffsets.Default, 1, 2, 1, null, 365);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(Token);
        }
        var one = await Closing(e, x => x.LimitSuggestionsAsync(dx.Id, Token));
        Assert.Equal(new[] { onTime, late, reduced }.Order(), one.Items.Select(i => i.FarmerProfileId).Order());
        var htOnly = await Closing(e, x => x.LimitSuggestionsAsync(ht.Id, Token));
        Assert.Equal(new[] { onTime, onlyHt }.Order(), htOnly.Items.Select(i => i.FarmerProfileId).Order());
        // Nothing changed any credit limit.
        using var verify = e.Scope();
        Assert.All(await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().FarmerCreditProfiles.AsNoTracking().ToListAsync(Token),
            p => Assert.Equal(20_000_000m, p.CreditLimit));
    }
}
