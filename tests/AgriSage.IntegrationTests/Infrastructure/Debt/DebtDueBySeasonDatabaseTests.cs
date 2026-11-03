using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.13 (D1, D4, D15, D17; owner L3 choice B) against real PostgreSQL: the debt of a credit order arises on delivery with the
// season's due date, the tier term after it, or the tier term when the order has no season. The fixture's tier term is 30 days.
// Đông Xuân: harvest 15/02–15/03/2027, due 14/04/2027.
public sealed class DebtDueBySeasonDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CreateCropSeasonRequest DongXuan =
        new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1));

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task<CropSeasonResponse> Season(CreditTestDatabase e, CreateCropSeasonRequest request) =>
        Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(request, Token));

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    // Confirm a credit order, give it the season (what the confirmation does from the next commit on), deliver it on the day.
    private static async Task<OrderResponse> ConfirmAsync(CreditTestDatabase e, long quantity, Guid? season)
    {
        var order = await e.OrderAsync(quantity, Token);
        await Run(e, sp => sp.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(order.Id, Token));
        if (season is { } id)
        {
            using var s = e.Scope();
            await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE orders SET crop_season_id = {id} WHERE id = {order.Id}", Token);
        }

        return order;
    }

    private static Task PickupAsync(CreditTestDatabase e, OrderResponse order, long quantity) =>
        Run(e, sp => sp.GetRequiredService<IOrderPickupService>().PickupAsync(order.Id,
            new([new(order.Items.Single().Id, [new(e.LotId, quantity)])]), Token));

    private static async Task<List<DebtEntry>> EntriesAsync(CreditTestDatabase e, Guid orderId)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .Where(x => x.OrderId == orderId).OrderBy(x => x.EntryNumber).ToListAsync(Token);
    }

    private static JsonElement Basis(DebtEntry entry) => JsonDocument.Parse(entry.DueBasis!).RootElement;

    [CreditDbFact]
    public async Task A_delivery_before_the_season_due_date_takes_the_season_due_date()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var order = await ConfirmAsync(e, 3, dx.Id);
        At(e, 2026, 12, 6);
        await PickupAsync(e, order, 3);

        var entry = Assert.Single(await EntriesAsync(e, order.Id));
        Assert.Equal((new DateOnly(2027, 4, 14), new DateOnly(2027, 4, 14), dx.Id, dx.Id, 3_000_000m),
            (entry.DueDate, entry.OriginalDueDate, entry.OriginCropSeasonId!.Value, entry.CollectionCropSeasonId!.Value, entry.OutstandingAmount));
        var basis = Basis(entry);
        Assert.Equal(("SEASON", dx.Id, "DX-2026", "2027-03-15", 30, 30, "2026-12-06", false),
            (basis.GetProperty("rule").GetString(), basis.GetProperty("cropSeasonId").GetGuid(), basis.GetProperty("seasonCode").GetString(),
                basis.GetProperty("harvestEnd").GetString(), basis.GetProperty("graceDays").GetInt32(), basis.GetProperty("termDays").GetInt32(),
                basis.GetProperty("fulfilledOn").GetString(), basis.GetProperty("lateForSeason").GetBoolean()));
    }

    // Owner choice B: a delivery after the season due date is not overdue on day one; the customer gets the tier term.
    [CreditDbFact]
    public async Task A_delivery_after_the_season_due_date_gets_the_tier_term_from_the_delivery_day()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var late = await ConfirmAsync(e, 2, dx.Id);
        var onTheDay = await ConfirmAsync(e, 1, dx.Id);
        At(e, 2027, 5, 20);
        await PickupAsync(e, late, 2);
        At(e, 2027, 4, 14);
        await PickupAsync(e, onTheDay, 1);

        var entry = Assert.Single(await EntriesAsync(e, late.Id));
        Assert.Equal((new DateOnly(2027, 6, 19), new DateOnly(2027, 6, 19), dx.Id), (entry.DueDate, entry.OriginalDueDate, entry.OriginCropSeasonId!.Value));
        Assert.Equal(("TERM_DAYS", true, 30, "2027-05-20"), (Basis(entry).GetProperty("rule").GetString(), Basis(entry).GetProperty("lateForSeason").GetBoolean(),
            Basis(entry).GetProperty("termDays").GetInt32(), Basis(entry).GetProperty("fulfilledOn").GetString()));
        // Delivered on the due date itself: due that day, not late.
        var same = Assert.Single(await EntriesAsync(e, onTheDay.Id));
        Assert.Equal((new DateOnly(2027, 4, 14), "SEASON"), (same.DueDate, Basis(same).GetProperty("rule").GetString()));
    }

    [CreditDbFact]
    public async Task An_order_without_a_season_keeps_the_tier_term_as_before()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Season(e, DongXuan);
        var order = await ConfirmAsync(e, 2, null);
        At(e, 2026, 12, 6);
        await PickupAsync(e, order, 2);

        var entry = Assert.Single(await EntriesAsync(e, order.Id));
        Assert.Equal((new DateOnly(2027, 1, 5), (Guid?)null, (Guid?)null, "TERM_DAYS"),
            (entry.DueDate, entry.OriginCropSeasonId, entry.CollectionCropSeasonId, Basis(entry).GetProperty("rule").GetString()));
        Assert.Equal(JsonValueKind.Null, Basis(entry).GetProperty("cropSeasonId").ValueKind);
    }

    // D4: the store's optional cap shortens a season due date to delivery + cap; it never shortens the tier term.
    [CreditDbFact]
    public async Task The_store_cap_shortens_only_the_season_due_date()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(30, DebtReminderOffsets.Default, 1, 2, 2, 60, 365);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(Token);
        }

        var capped = await ConfirmAsync(e, 1, dx.Id);
        var noSeason = await ConfirmAsync(e, 1, null);
        At(e, 2026, 12, 6);
        await PickupAsync(e, capped, 1);
        await PickupAsync(e, noSeason, 1);

        var entry = Assert.Single(await EntriesAsync(e, capped.Id));
        Assert.Equal((new DateOnly(2027, 2, 4), "CAPPED", 60), (entry.DueDate, Basis(entry).GetProperty("rule").GetString(), Basis(entry).GetProperty("capDays").GetInt32()));
        Assert.Equal(new DateOnly(2027, 1, 5), Assert.Single(await EntriesAsync(e, noSeason.Id)).DueDate);
    }

    // D15: confirming reserves credit but creates no debt; each delivery creates its own debt of the same season.
    [CreditDbFact]
    public async Task Debt_arises_on_delivery_one_entry_per_delivery()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var order = await ConfirmAsync(e, 4, dx.Id);
        Assert.Empty(await EntriesAsync(e, order.Id));
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            Assert.Equal(0m, (await db.DebtAccounts.AsNoTracking().SingleAsync(Token)).CurrentBalance);
            Assert.Equal(4_000_000m, await db.CreditReservations.AsNoTracking().SumAsync(r => r.AmountReserved, Token));
        }

        At(e, 2026, 12, 6);
        await PickupAsync(e, order, 1);
        At(e, 2027, 1, 11);
        await PickupAsync(e, order, 3);

        var entries = await EntriesAsync(e, order.Id);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, x => Assert.Equal((dx.Id, new DateOnly(2027, 4, 14)), (x.OriginCropSeasonId!.Value, x.DueDate)));
        Assert.Equal(new[] { 1_000_000m, 3_000_000m }.Order(), entries.Select(x => x.OriginalAmount).Order());
    }

    [CreditDbFact]
    public async Task A_manual_debt_can_belong_to_a_season()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        At(e, 2026, 12, 6);
        Task<DebtEntryResponse> Manual(ManualDebtEntryRequest request) =>
            Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, request, Token));
        Task<DebtEntry> Stored(Guid id) => Run(e, async sp => await sp.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.Id == id, Token));

        var bySeason = await Manual(new(500_000m, null, "Ghi nợ cũ", dx.Id));
        Assert.Equal(new DateOnly(2027, 4, 14), bySeason.DueDate);
        var stored = await Stored(bySeason.Id);
        Assert.Equal((dx.Id, dx.Id, new DateOnly(2027, 4, 14), "SEASON"),
            (stored.OriginCropSeasonId!.Value, stored.CollectionCropSeasonId!.Value, stored.OriginalDueDate, Basis(stored).GetProperty("rule").GetString()));
        Assert.Equal("DX-2026", bySeason.OriginSeason!.Code);

        var chosen = await Manual(new(200_000m, new DateOnly(2027, 6, 1), "Hẹn riêng", dx.Id));
        Assert.Equal(("MANUAL", new DateOnly(2027, 6, 1)), (Basis(await Stored(chosen.Id)).GetProperty("rule").GetString(), chosen.DueDate));

        // Without a season it works as before: a date is required and no season is recorded.
        var plain = await Manual(new(100_000m, new DateOnly(2026, 12, 20), "Không vụ"));
        Assert.Equal(((Guid?)null, (string?)null), ((await Stored(plain.Id)).OriginCropSeasonId, (await Stored(plain.Id)).DueBasis));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Manual(new(100_000m, null, "Thiếu hạn")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Manual(new(100_000m, null, "Vụ lạ", Guid.NewGuid())));

        At(e, 2027, 5, 1); // past the season due date: a date has to be given
        await Assert.ThrowsAsync<BusinessRuleException>(() => Manual(new(100_000m, null, "Quá hạn vụ", dx.Id)));
        Assert.Equal(new DateOnly(2027, 5, 31), (await Manual(new(100_000m, new DateOnly(2027, 5, 31), "Có hạn", dx.Id))).DueDate);

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var season = await db.CropSeasons.SingleAsync(x => x.Id == dx.Id, Token);
            season.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 5, 1));
            await db.SaveChangesAsync(Token);
        }
        await Assert.ThrowsAsync<BusinessRuleException>(() => Manual(new(100_000m, new DateOnly(2027, 6, 1), "Vụ đã đóng", dx.Id)));
    }

    // CS-04: the credit check tells the expected season and due date, and how the date was reached.
    [CreditDbFact]
    public async Task The_credit_check_reports_the_expected_season_and_due_date()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        Task<CreditEligibilityResponse> Check() =>
            Run(e, sp => sp.GetRequiredService<ICreditEligibilityService>().CheckAsync(e.FarmerId, 1_000_000m, Token));

        var none = await Check();
        Assert.Equal(((DebtSeasonReference?)null, new DateOnly(2026, 11, 4), "TERM_DAYS"), (none.ExpectedCropSeason, none.ExpectedDueDate, none.DueRule));

        var dx = await Season(e, DongXuan);
        var inSeason = await Check();
        Assert.Equal(("DX-2026", new DateOnly(2027, 4, 14), "SEASON", true),
            (inSeason.ExpectedCropSeason!.Code, inSeason.ExpectedDueDate, inSeason.DueRule, inSeason.Eligible));
        Assert.Equal(dx.Id, inSeason.ExpectedCropSeason.Id);

        // Not eligible (over the limit) still shows the estimate; overdue debt does not change it (CS-10).
        var over = await Run(e, sp => sp.GetRequiredService<ICreditEligibilityService>().CheckAsync(e.FarmerId, 30_000_000m, Token));
        Assert.Equal((false, "INSUFFICIENT_CREDIT", new DateOnly(2027, 4, 14)), (over.Eligible, over.ReasonCode, over.ExpectedDueDate));
    }
}
