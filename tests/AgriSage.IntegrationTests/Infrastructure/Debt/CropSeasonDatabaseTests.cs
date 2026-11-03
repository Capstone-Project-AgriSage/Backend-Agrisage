using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.11 against real PostgreSQL. The fixture's today is 05/10/2026 (Vietnam): Đông Xuân 2026–2027 is taking orders,
// Hè Thu 2027 is planned.
public sealed class CropSeasonDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CreateCropSeasonRequest DongXuan =
        new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1));

    private static readonly CreateCropSeasonRequest HeThu =
        new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<ICropSeasonService, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider.GetRequiredService<ICropSeasonService>());
    }

    [CreditDbFact]
    public async Task Owner_creates_chained_seasons_and_the_store_rules_hold()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.CreateAsync(DongXuan with { OrderWindowStart = null }, Token)));
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        Assert.Equal(("DX-2026", "RICE", new DateOnly(2026, 10, 1), new DateOnly(2027, 4, 14), "OPEN", 0L),
            (dx.Code, dx.CropType, dx.OrderWindowStart, dx.PaymentDueDate, dx.Status, dx.Version));

        // The next season opens on the previous harvest start; another date is a gap or an overlap.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.CreateAsync(HeThu with { OrderWindowStart = new DateOnly(2027, 2, 20) }, Token)));
        await Assert.ThrowsAsync<ConflictException>(() => Run(e, s => s.CreateAsync(HeThu with { Code = "DX-2026" }, Token)));
        // Only after the last season: a harvest before the last harvest start is refused.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.CreateAsync(HeThu with { HarvestStart = new DateOnly(2027, 1, 1), HarvestEnd = new DateOnly(2027, 1, 30) }, Token)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.CreateAsync(HeThu with { SeasonalRuleId = Guid.NewGuid() }, Token)));

        var ht = await Run(e, s => s.CreateAsync(HeThu, Token));
        Assert.Equal((new DateOnly(2027, 2, 15), new DateOnly(2027, 9, 14), "PLANNED"), (ht.OrderWindowStart, ht.PaymentDueDate, ht.Status));

        Assert.Equal(new[] { "HT-2027", "DX-2026" }, (await Run(e, s => s.ListAsync(new(), Token))).Items.Select(i => i.Code));
        Assert.Equal("DX-2026", Assert.Single((await Run(e, s => s.ListAsync(new() { Status = "OPEN" }, Token))).Items).Code);
        Assert.Equal("HT-2027", Assert.Single((await Run(e, s => s.ListAsync(new() { Status = "PLANNED" }, Token))).Items).Code);
        Assert.Empty((await Run(e, s => s.ListAsync(new() { Year = 2026 }, Token))).Items);
        Assert.Equal(2, (await Run(e, s => s.ListAsync(new() { Year = 2027 }, Token))).TotalCount);

        using var verify = e.Scope();
        var audits = await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
            .CountAsync(a => a.Action == "CROP_SEASON_CREATED", Token);
        Assert.Equal(2, audits);
    }

    [CreditDbFact]
    public async Task The_first_grace_default_comes_from_the_store_debt_policy()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(45, DebtReminderOffsets.Default, 1, 2, 2, null, 365);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(Token);
        }
        var season = await Run(e, s => s.CreateAsync(DongXuan with { GraceDays = null }, Token));
        Assert.Equal((45, new DateOnly(2027, 4, 29)), (season.GraceDays, season.PaymentDueDate));
    }

    [CreditDbFact]
    public async Task Moving_a_harvest_start_moves_the_next_order_window_and_a_stale_version_is_refused()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        var ht = await Run(e, s => s.CreateAsync(HeThu, Token));

        var updated = await Run(e, s => s.UpdateAsync(dx.Id, new("Đông Xuân", new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 20), 30, dx.Version), Token));
        Assert.Equal((new DateOnly(2027, 4, 19), 1L), (updated.PaymentDueDate, updated.Version));
        Assert.Equal(new DateOnly(2027, 2, 20), (await Run(e, s => s.GetAsync(ht.Id, Token))).OrderWindowStart);

        await Assert.ThrowsAsync<ConflictException>(() => Run(e, s => s.UpdateAsync(dx.Id, new("X", new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 20), 30, dx.Version), Token)));
        // The harvest cannot start after the next season's harvest.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.UpdateAsync(dx.Id, new("X", new DateOnly(2027, 7, 20), new DateOnly(2027, 7, 30), 30, updated.Version), Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => Run(e, s => s.UpdateAsync(Guid.NewGuid(), new("X", new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 20), 30, 0), Token)));
    }

    // D17: by default recorded debt keeps its due date. Applying moves only entries still on the old season due date, only
    // later, with a CHANGE_DUE_DATE action per entry.
    [CreditDbFact]
    public async Task Debts_follow_a_later_season_due_date_only_when_the_owner_asks()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        var follows = await e.FulfillAsync(5, Token);
        var own = await e.FulfillAsync(2, Token);
        Guid followsId, ownId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            followsId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == follows.Id, Token)).Id;
            ownId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == own.Id, Token)).Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET origin_crop_season_id = {dx.Id}, collection_crop_season_id = {dx.Id}, due_date = {dx.PaymentDueDate} WHERE id = {followsId}", Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET origin_crop_season_id = {dx.Id}, collection_crop_season_id = {dx.Id}, due_date = {new DateOnly(2027, 2, 1)} WHERE id = {ownId}", Token);
        }

        var figures = await Run(e, s => s.GetAsync(dx.Id, Token));
        Assert.Equal((7_000_000m, 2, 7_000_000m, 2), (figures.Originated.OutstandingAmount, figures.Originated.OpenEntryCount,
            figures.Collecting.OutstandingAmount, figures.Collecting.OpenEntryCount));

        var kept = await Run(e, s => s.UpdateAsync(dx.Id, new("Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 25), 30, dx.Version), Token));
        Assert.Equal(new DateOnly(2027, 4, 14), await DueAsync(e, followsId));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.UpdateAsync(dx.Id,
            new("Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 10), 30, kept.Version, ApplyNewDueDateToCollectingDebts: true, Reason: "Earlier"), Token)));

        // The season due date is now 24/04; entries still on it move together with the next change.
        await Run(e, s => s.UpdateAsync(dx.Id, new("Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 25), 40, kept.Version,
            ApplyNewDueDateToCollectingDebts: true, Reason: "Lũ làm trễ thu hoạch"), Token));
        Assert.Equal(new DateOnly(2027, 4, 14), await DueAsync(e, followsId)); // it was on the old 14/04, not on 24/04

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET due_date = {new DateOnly(2027, 5, 4)} WHERE id = {followsId}", Token);
        }
        var current = await Run(e, s => s.GetAsync(dx.Id, Token));
        await Run(e, s => s.UpdateAsync(dx.Id, new("Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 25), 60, current.Version,
            ApplyNewDueDateToCollectingDebts: true, Reason: "Lũ làm trễ thu hoạch"), Token));
        Assert.Equal(new DateOnly(2027, 5, 24), await DueAsync(e, followsId));
        Assert.Equal(new DateOnly(2027, 2, 1), await DueAsync(e, ownId));
        using var verify = e.Scope();
        var action = await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntryActions.AsNoTracking()
            .SingleAsync(a => a.DebtEntryId == followsId, Token);
        Assert.Equal((DebtEntryActionType.ChangeDueDate, new DateOnly(2027, 5, 4), new DateOnly(2027, 5, 24), "Lũ làm trễ thu hoạch"),
            (action.ActionType, action.OldDueDate!.Value, action.NewDueDate!.Value, action.Reason));
    }

    [CreditDbFact]
    public async Task Sale_staff_cannot_create_or_change_seasons_even_with_the_permission()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        var owner = e.User.UserId;
        e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(e, s => s.CreateAsync(HeThu, Token)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(e, s => s.UpdateAsync(dx.Id, new("X", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, dx.Version), Token)));
        Assert.Single((await Run(e, s => s.ListAsync(new(), Token))).Items);
        e.User.UserId = owner; e.User.Role = "STORE_OWNER";
    }

    [CreditDbFact]
    public async Task A_closed_season_cannot_change()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var season = await db.CropSeasons.SingleAsync(Token);
            season.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 6, 1));
            await db.SaveChangesAsync(Token);
        }
        var closed = await Run(e, s => s.GetAsync(dx.Id, Token));
        Assert.Equal("CLOSED", closed.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, s => s.UpdateAsync(dx.Id, new("X", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, closed.Version), Token)));
    }

    // CS-04: the Farmer sees the season and the expected due date before ordering on credit.
    [CreditDbFact]
    public async Task Farmer_sees_the_current_season_or_the_tier_term()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var owner = e.User.UserId;
        e.User.UserId = e.FarmerUserId; e.User.Role = "FARMER";
        var none = await Run(e, s => s.MyCurrentAsync(Token));
        Assert.Equal(("TERM_DAYS", 30, new DateOnly(2026, 11, 4), (Guid?)null), (none.DueRule, none.TermDays!.Value, none.ExpectedDueDate!.Value, none.CropSeasonId));

        e.User.UserId = owner; e.User.Role = "STORE_OWNER";
        var dx = await Run(e, s => s.CreateAsync(DongXuan, Token));
        e.User.UserId = e.FarmerUserId; e.User.Role = "FARMER";
        var current = await Run(e, s => s.MyCurrentAsync(Token));
        Assert.Equal((dx.Id, "SEASON", new DateOnly(2027, 4, 14), true), (current.CropSeasonId!.Value, current.DueRule, current.ExpectedDueDate!.Value, current.IsEstimate));

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var policy = new StoreDebtPolicy(e.StoreId);
            policy.Update(30, DebtReminderOffsets.Default, 1, 2, 2, 90, 365);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(Token);
        }
        var capped = await Run(e, s => s.MyCurrentAsync(Token));
        Assert.Equal(("CAPPED", new DateOnly(2027, 1, 3)), (capped.DueRule, capped.ExpectedDueDate!.Value));
        e.User.UserId = owner; e.User.Role = "STORE_OWNER";
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(e, s => s.MyCurrentAsync(Token)));
    }

    private static async Task<DateOnly> DueAsync(CreditTestDatabase e, Guid entryId)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .Where(x => x.Id == entryId).Select(x => x.DueDate).SingleAsync(Token);
    }
}
