using System.Text.Json;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.13 (D15, CS-04) against real PostgreSQL: a credit order joins the season whose order window contains its confirmation day
// (Vietnam time), keeps it, and its debt arises on delivery. Seasons: Đông Xuân orders 01/10/2026–14/02/2027, harvest
// 15/02–15/03/2027, due 14/04/2027; Hè Thu orders from 15/02/2027, harvest 15/07–15/08/2027, due 14/09/2027.
// The fixture's price is 1,000,000 per unit, so amounts are multiples of it.
public sealed class OrderSeasonConfirmationDatabaseTests
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

    private static async Task<(CropSeasonResponse Dx, CropSeasonResponse Ht)> SeasonsAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var seasons = s.ServiceProvider.GetRequiredService<ICropSeasonService>();
        return (await seasons.CreateAsync(DongXuan, Token), await seasons.CreateAsync(HeThu, Token));
    }

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static async Task<OrderResponse> ConfirmAsync(CreditTestDatabase e, long quantity)
    {
        var order = await e.OrderAsync(quantity, Token);
        return await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
    }

    private static Task<OrderResponse> PickupAsync(CreditTestDatabase e, OrderResponse order, long quantity) =>
        Run(e, sp => sp.GetRequiredService<IOrderPickupService>().PickupAsync(order.Id,
            new([new(order.Items.Single().Id, [new(e.LotId, quantity)])]), Token));

    private static async Task<List<DebtEntry>> DebtsAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().OrderBy(x => x.EntryNumber).ToListAsync(Token);
    }

    private static async Task<Guid?> StoredSeasonAsync(CreditTestDatabase e, Guid orderId)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, Token)).CropSeasonId;
    }

    // §8.1 "nông dân Bảy" (amounts scaled to the fixture price): orders before the harvest belong to Đông Xuân and are due
    // 14/04/2027; an order confirmed while the harvest is on (20/02) belongs to Hè Thu, due 14/09/2027.
    [CreditDbFact]
    public async Task Farmer_Bay_orders_join_the_season_of_their_confirmation_day()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);

        At(e, 2026, 12, 6);
        var a = await ConfirmAsync(e, 3);
        Assert.Equal(("DX-2026", new DateOnly(2027, 4, 14), "CONFIRMED"), (a.OriginSeason!.Code, a.ExpectedDueDate, a.Status));
        Assert.Equal(dx.Id, await StoredSeasonAsync(e, a.Id));
        Assert.Empty(await DebtsAsync(e));                                           // confirmed, not delivered: no debt (D15)
        var afterA = await PickupAsync(e, a, 3);
        Assert.Equal(("COMPLETED", dx.Id, (DateOnly?)null), (afterA.Status, afterA.OriginSeason!.Id, afterA.ExpectedDueDate));

        At(e, 2027, 1, 11);
        var b = await ConfirmAsync(e, 2);
        await PickupAsync(e, b, 2);

        At(e, 2027, 2, 20);                                                          // the harvest of Đông Xuân is on
        var c = await ConfirmAsync(e, 1);
        Assert.Equal(("HT-2027", new DateOnly(2027, 9, 14)), (c.OriginSeason!.Code, c.ExpectedDueDate));
        await PickupAsync(e, c, 1);

        var debts = await DebtsAsync(e);
        Assert.Equal(3, debts.Count);
        var byOrder = debts.ToDictionary(x => x.OrderId!.Value);
        Assert.Equal((dx.Id, new DateOnly(2027, 4, 14), 3_000_000m), (byOrder[a.Id].OriginCropSeasonId!.Value, byOrder[a.Id].DueDate, byOrder[a.Id].OutstandingAmount));
        Assert.Equal((dx.Id, new DateOnly(2027, 4, 14)), (byOrder[b.Id].OriginCropSeasonId!.Value, byOrder[b.Id].DueDate));
        Assert.Equal((ht.Id, ht.Id, new DateOnly(2027, 9, 14), 0), (byOrder[c.Id].OriginCropSeasonId!.Value, byOrder[c.Id].CollectionCropSeasonId!.Value,
            byOrder[c.Id].DueDate, byOrder[c.Id].RolloverCount));

        // The report follows the origin season: Đông Xuân 5 million, Hè Thu 1 million.
        using var s = e.Scope();
        var report = await s.ServiceProvider.GetRequiredService<ICropSeasonClosingService>().ReportAsync(new() { PageSize = 10 }, Token);
        Assert.Equal((5_000_000m, 1_000_000m), (report.Items.Single(i => i.Season.Id == dx.Id).Origin.OutstandingAmount,
            report.Items.Single(i => i.Season.Id == ht.Id).Origin.OutstandingAmount));
    }

    // The order window changes at midnight Vietnam time, which is 17:00 UTC.
    [CreditDbFact]
    public async Task The_season_changes_at_midnight_Vietnam_time()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);

        e.Clock.UtcNow = new DateTimeOffset(2027, 2, 14, 16, 59, 59, TimeSpan.Zero);   // 14/02 23:59:59 in Vietnam
        var before = await ConfirmAsync(e, 1);
        e.Clock.UtcNow = new DateTimeOffset(2027, 2, 14, 17, 0, 0, TimeSpan.Zero);     // 15/02 00:00:00: the harvest day
        var after = await ConfirmAsync(e, 1);

        Assert.Equal(dx.Id, before.OriginSeason!.Id);
        Assert.Equal(ht.Id, after.OriginSeason!.Id);
        Assert.Equal(dx.Id, await StoredSeasonAsync(e, before.Id));
        Assert.Equal(ht.Id, await StoredSeasonAsync(e, after.Id));
    }

    // No season for the day (none entered, or the day is past the last one): the tier term, as before.
    [CreditDbFact]
    public async Task Without_a_season_the_order_keeps_the_tier_term()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 12, 6);
        var none = await ConfirmAsync(e, 2);
        Assert.Equal(((DebtSeasonReference?)null, new DateOnly(2027, 1, 5), (Guid?)null), (none.OriginSeason, none.ExpectedDueDate, await StoredSeasonAsync(e, none.Id)));
        await PickupAsync(e, none, 2);
        var entry = Assert.Single(await DebtsAsync(e));
        Assert.Equal((new DateOnly(2027, 1, 5), (Guid?)null, "TERM_DAYS"),
            (entry.DueDate, entry.OriginCropSeasonId, JsonDocument.Parse(entry.DueBasis!).RootElement.GetProperty("rule").GetString()));

        // Seasons entered later do not reach back to an order already confirmed.
        var later = await ConfirmAsync(e, 1);
        await SeasonsAsync(e);
        Assert.Null(await StoredSeasonAsync(e, later.Id));
        await PickupAsync(e, later, 1);
        Assert.Null((await DebtsAsync(e)).Single(x => x.OrderId == later.Id).OriginCropSeasonId);

        At(e, 2027, 9, 1);                                                             // after the last season's order window
        var past = await ConfirmAsync(e, 1);
        Assert.Null(past.OriginSeason);
    }

    // The season is fixed at confirmation: editing its dates later never moves the order to another season.
    [CreditDbFact]
    public async Task Editing_the_season_afterwards_does_not_move_a_confirmed_order()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, ht) = await SeasonsAsync(e);
        At(e, 2027, 2, 10);
        var order = await ConfirmAsync(e, 2);
        Assert.Equal(dx.Id, order.OriginSeason!.Id);

        await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().UpdateAsync(dx.Id,
            new("Đông Xuân", new DateOnly(2027, 2, 5), new DateOnly(2027, 3, 15), 30, dx.Version), Token));   // harvest starts earlier
        Assert.Equal(dx.Id, await StoredSeasonAsync(e, order.Id));
        Assert.Equal(ht.Id, (await ConfirmAsync(e, 1)).OriginSeason!.Id);               // a new order of the same day joins Hè Thu
    }

    // CS-10: overdue debt never refuses credit by itself; the new order still joins the season.
    [CreditDbFact]
    public async Task Overdue_debt_does_not_stop_a_credit_order_within_the_limit()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, _) = await SeasonsAsync(e);
        At(e, 2026, 12, 6);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(1_000_000m, new DateOnly(2026, 12, 1), "Nợ cũ quá hạn"), Token));
        var check = await Run(e, sp => sp.GetRequiredService<ICreditEligibilityService>().CheckAsync(e.FarmerId, 2_000_000m, Token));
        Assert.Equal((true, true, 1_000_000m), (check.Eligible, check.HasOverdueDebt, check.OverdueAmount));

        var order = await ConfirmAsync(e, 2);

        Assert.Equal((dx.Id, "CONFIRMED"), (order.OriginSeason!.Id, order.Status));
    }

    [CreditDbFact]
    public async Task The_confirmation_audit_records_the_season()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, _) = await SeasonsAsync(e);
        At(e, 2026, 12, 6);
        var order = await ConfirmAsync(e, 1);

        using var s = e.Scope();
        var audit = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "ORDER_CONFIRMED" && a.EntityId == order.Id, Token);
        var values = JsonDocument.Parse(audit.NewValues!).RootElement;
        Assert.Equal((dx.Id, 30), (values.GetProperty("cropSeasonId").GetGuid(), values.GetProperty("creditTermDays").GetInt32()));
    }
}
