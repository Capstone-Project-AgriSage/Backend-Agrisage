using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.15 against real PostgreSQL: rolling a whole season's debt over to a later season, entry by entry with the F3.14 rules.
// The fixture's today is 05/10/2026 (Vietnam). Đông Xuân is due 14/04/2027, Hè Thu 14/09/2027, Thu Đông 14/01/2028.
public sealed class CropSeasonRolloverDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly DateOnly DxDue = new(2027, 4, 14);
    private static readonly DateOnly HtDue = new(2027, 9, 14);
    private static readonly DateOnly TdDue = new(2028, 1, 14);

    private sealed record Setup(CreditTestDatabase E, Guid Dx, Guid Ht, Guid Td, Guid A, Guid B, Guid Other, Guid OtherFarmer,
        Guid Disputed, Guid RolledOnce);

    private static CropSeasonRolloverRequest Request(Guid? target = null, IReadOnlyList<Guid>? ids = null, bool dryRun = false,
        bool overrideLimit = false) => new("MAT_MUA", "Mất mùa cả vụ", target, ids, dryRun, overrideLimit);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task<CropSeasonRolloverResponse> Roll(CreditTestDatabase e, Guid season, CropSeasonRolloverRequest request) =>
        Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverSeasonAsync(season, request, Token));

    private static async Task<Guid> SeasonAsync(CreditTestDatabase e, CreateCropSeasonRequest request) =>
        (await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(request, Token))).Id;

    private static async Task<Guid> DebtAsync(CreditTestDatabase e, long quantity)
    {
        var order = await e.FulfillAsync(quantity, Token);
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id, Token)).Id;
    }

    private static async Task SetAsync(CreditTestDatabase e, Guid id, Guid season, DateOnly due, DateOnly? original = null, int rollovers = 0)
    {
        using var s = e.Scope();
        await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE debt_entries SET origin_crop_season_id = {season}, collection_crop_season_id = {season}, due_date = {due}, original_due_date = {original ?? due}, rollover_count = {rollovers} WHERE id = {id}", Token);
    }

    // Two customers with debt collected in Đông Xuân: A, B (the fixture Farmer) and Other (a second Farmer); a disputed
    // entry and one already rolled over once (Sale staff may not roll it again).
    private static async Task<Setup> SetupAsync(CreditTestDatabase e)
    {
        var dx = await SeasonAsync(e, new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)));
        var ht = await SeasonAsync(e, new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30));
        var td = await SeasonAsync(e, new("TD-2027", "Thu Đông 2027", new DateOnly(2027, 11, 15), new DateOnly(2027, 12, 15), 30));
        var a = await DebtAsync(e, 2);
        var b = await DebtAsync(e, 3);
        var disputed = await DebtAsync(e, 1);
        var rolledOnce = await DebtAsync(e, 1);
        Guid other, otherFarmer;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
            var user = new User(role.Id, "Other farmer", "hash", "other@example.test", "0987654321");
            var farmer = new FarmerProfile(user.Id);
            var account = new DebtAccount(e.StoreId, farmer.Id);
            var posting = account.CreateManualAdjustmentEntry("DE-OTHER", 4_000_000m, DxDue, e.User.UserId!.Value, e.Clock.UtcNow);
            db.AddRange(user, farmer, account, posting.Entry, posting.Transaction);
            await db.SaveChangesAsync(Token);
            (other, otherFarmer) = (posting.Entry.Id, farmer.Id);
        }

        foreach (var id in new[] { a, b, other, disputed }) await SetAsync(e, id, dx, DxDue);
        await SetAsync(e, rolledOnce, dx, DxDue, original: new DateOnly(2026, 11, 4), rollovers: 1);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "DISPUTE", "Sai số lượng", null, null, Token));
        return new(e, dx, ht, td, a, b, other, otherFarmer, disputed, rolledOnce);
    }

    private static async Task<Dictionary<Guid, DebtEntry>> EntriesAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().Include(x => x.Actions)
            .ToDictionaryAsync(x => x.Id, Token);
    }

    private static async Task<(int Audits, int Outbox, int Ledger, decimal Balance)> TraceAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "DEBT_ROLLOVER").Select(a => a.Id).ToListAsync(Token);
        return (audits.Count, await db.NotificationOutbox.CountAsync(o => audits.Contains(o.AuditLogId), Token),
            await db.DebtTransactions.CountAsync(Token), await db.DebtAccounts.SumAsync(x => x.CurrentBalance, Token));
    }

    private static async Task<Guid> RolloverStaffAsync(CreditTestDatabase e, bool grant = true)
    {
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        if (grant)
        {
            using var s = e.Scope();
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            db.StoreMemberPermissions.Add(new StoreMemberPermission(staff.MemberId, PermissionCatalog.ByCode["DEBT.ROLLOVER"].Id, true));
            await db.SaveChangesAsync(Token);
        }

        return staff.UserId;
    }

    private static async Task As(CreditTestDatabase e, Guid user, string role, Func<Task> act)
    {
        var (oldUser, oldRole) = (e.User.UserId, e.User.Role);
        e.User.UserId = user; e.User.Role = role;
        try { await act(); }
        finally { e.User.UserId = oldUser; e.User.Role = oldRole; }
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();

    [CreditDbFact]
    public async Task A_dry_run_reports_the_result_and_writes_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var before = await EntriesAsync(e);
        var trace = await TraceAsync(e);

        var preview = await Roll(e, x.Dx, Request(dryRun: true));

        Assert.True(preview.DryRun);
        Assert.Equal((x.Ht, "HT-2027", HtDue), (preview.TargetSeason.Id, preview.TargetSeason.Code, preview.NewDueDate));
        Assert.Equal(Sorted(x.A, x.B, x.Other, x.RolledOnce), preview.RolledOver.Select(r => r.EntryId).Order().ToList());
        Assert.Equal(("DISPUTED", x.Disputed), (Assert.Single(preview.Skipped).Reason, preview.Skipped[0].EntryId));
        var after = await EntriesAsync(e);
        Assert.All(before, kv => Assert.Equal((kv.Value.DueDate, kv.Value.CollectionCropSeasonId, kv.Value.RolloverCount, kv.Value.Actions.Count),
            (after[kv.Key].DueDate, after[kv.Key].CollectionCropSeasonId, after[kv.Key].RolloverCount, after[kv.Key].Actions.Count)));
        Assert.Equal(trace, await TraceAsync(e));
    }

    [CreditDbFact]
    public async Task The_owner_rolls_the_season_over_and_a_second_call_skips_everything()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var trace = await TraceAsync(e);

        var result = await Roll(e, x.Dx, Request());

        Assert.False(result.DryRun);
        Assert.Equal(Sorted(x.A, x.B, x.Other, x.RolledOnce), result.RolledOver.Select(r => r.EntryId).Order().ToList());
        var rolledA = result.RolledOver.Single(r => r.EntryId == x.A);
        Assert.Equal((e.FarmerId, DxDue, HtDue, 2_000_000m), (rolledA.FarmerProfileId, rolledA.OldDueDate, rolledA.NewDueDate, rolledA.OutstandingAmount));
        Assert.Equal(x.OtherFarmer, result.RolledOver.Single(r => r.EntryId == x.Other).FarmerProfileId);
        Assert.Equal("DISPUTED", Assert.Single(result.Skipped).Reason);

        var entries = await EntriesAsync(e);
        foreach (var id in new[] { x.A, x.B, x.Other })
        {
            var entry = entries[id];
            Assert.Equal((HtDue, x.Ht, x.Dx, DxDue, 1), (entry.DueDate, entry.CollectionCropSeasonId!.Value, entry.OriginCropSeasonId!.Value,
                entry.OriginalDueDate, entry.RolloverCount));
            var action = Assert.Single(entry.Actions);
            Assert.Equal((DebtEntryActionType.Rollover, DebtReasonCode.MatMua, "Mất mùa cả vụ", x.Dx, x.Ht),
                (action.ActionType, action.ReasonCode!.Value, action.Reason, action.OldCropSeasonId!.Value, action.NewCropSeasonId!.Value));
        }

        Assert.Equal((DxDue, x.Dx, 0), (entries[x.Disputed].DueDate, entries[x.Disputed].CollectionCropSeasonId!.Value, entries[x.Disputed].RolloverCount));
        Assert.Equal(2, entries[x.RolledOnce].RolloverCount);
        // One audit and one Farmer notification per entry; the ledger and balances do not move.
        var now = await TraceAsync(e);
        Assert.Equal((trace.Audits + 4, trace.Outbox + 4, trace.Ledger, trace.Balance), now);
        using (var s = e.Scope())
        {
            var summary = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
                .SingleAsync(a => a.Action == "CROP_SEASON_DEBT_ROLLOVER", Token);
            Assert.Equal(("CROP_SEASON", x.Dx), (summary.EntityType, summary.EntityId!.Value));
        }

        // Again: nothing is left in Đông Xuân but the disputed entry, and the moved entries are no longer collected there.
        var again = await Roll(e, x.Dx, Request());
        Assert.Empty(again.RolledOver);
        Assert.Equal("DISPUTED", Assert.Single(again.Skipped).Reason);
        var listed = await Roll(e, x.Dx, Request(ids: [x.A, x.B, x.Other, x.RolledOnce]));
        Assert.Empty(listed.RolledOver);
        Assert.All(listed.Skipped, s => Assert.Equal("NOT_IN_SEASON", s.Reason));
        Assert.Equal(4, listed.Skipped.Count);
        Assert.Equal(now, await TraceAsync(e));
    }

    // CS-08: Sale staff roll over only entries still within the store's staff limit; the rest wait for the owner.
    [CreditDbFact]
    public async Task Sale_staff_skip_entries_that_need_the_owner()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var staff = await RolloverStaffAsync(e);

        CropSeasonRolloverResponse result = null!;
        await As(e, staff, "SALES_STAFF", async () => result = await Roll(e, x.Dx, Request()));

        Assert.Equal(Sorted(x.A, x.B, x.Other), result.RolledOver.Select(r => r.EntryId).Order().ToList());
        Assert.Equal(new[] { ("DISPUTED", x.Disputed), ("NEEDS_OWNER", x.RolledOnce) }.OrderBy(t => t.Item2),
            result.Skipped.Select(s => (s.Reason, s.EntryId)).OrderBy(t => t.EntryId));
        Assert.Equal(1, (await EntriesAsync(e))[x.RolledOnce].RolloverCount);
    }

    [CreditDbFact]
    public async Task Without_the_permission_nothing_is_rolled_over()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var staff = await RolloverStaffAsync(e, grant: false);

        await As(e, staff, "SALES_STAFF", () => Assert.ThrowsAsync<ForbiddenException>(() => Roll(e, x.Dx, Request())));
        await As(e, e.FarmerUserId, "FARMER", () => Assert.ThrowsAsync<ForbiddenException>(() => Roll(e, x.Dx, Request())));
        Assert.All((await EntriesAsync(e)).Values, entry => Assert.Equal(0, entry.Actions.Count(a => a.ActionType == DebtEntryActionType.Rollover)));
    }

    [CreditDbFact]
    public async Task A_mixed_list_rolls_over_what_qualifies_and_says_why_the_rest_is_skipped()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var paid = await DebtAsync(e, 1);
        var inHeThu = await DebtAsync(e, 1);
        var laterOwnDue = await DebtAsync(e, 1);
        await SetAsync(e, paid, x.Dx, DxDue);
        await SetAsync(e, inHeThu, x.Ht, HtDue);
        await SetAsync(e, laterOwnDue, x.Dx, new DateOnly(2027, 10, 1)); // its own due date is already past Hè Thu's
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(paid, "CANCEL", "Miễn nợ", null, null, "TRI_AN", Token));
        var unknown = Guid.NewGuid();

        var result = await Roll(e, x.Dx, Request(ids: [x.A, x.A, unknown, paid, inHeThu, laterOwnDue, x.Disputed]));

        Assert.Equal([x.A], result.RolledOver.Select(r => r.EntryId).ToList());
        var reasons = result.Skipped.ToDictionary(s => s.EntryId, s => s.Reason);
        Assert.Equal(5, reasons.Count);
        Assert.Equal(("NOT_FOUND", "NOTHING_OWED", "NOT_IN_SEASON", "NOT_LATER", "DISPUTED"),
            (reasons[unknown], reasons[paid], reasons[inHeThu], reasons[laterOwnDue], reasons[x.Disputed]));
        Assert.Null(result.Skipped.Single(s => s.EntryId == unknown).EntryNumber);
        Assert.NotNull(result.Skipped.Single(s => s.EntryId == paid).EntryNumber);
        var entries = await EntriesAsync(e);
        Assert.Equal((HtDue, 1), (entries[x.A].DueDate, entries[x.A].RolloverCount));
        Assert.Equal((DxDue, 0), (entries[x.B].DueDate, entries[x.B].RolloverCount));
    }

    // D5 per entry: the total extension from each entry's original due date.
    [CreditDbFact]
    public async Task Entries_past_the_total_extension_limit_need_the_owner_and_the_override()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var staff = await RolloverStaffAsync(e);
        // RolledOnce was first due 04/11/2026: Thu Đông (14/01/2028) is 436 days later. A was first due 14/04/2027: 275 days.
        CropSeasonRolloverResponse byStaff = null!;
        await As(e, staff, "SALES_STAFF", async () => byStaff = await Roll(e, x.Dx, Request(x.Td, [x.A, x.RolledOnce], dryRun: true)));
        Assert.Equal([x.A], byStaff.RolledOver.Select(r => r.EntryId).ToList());
        Assert.Equal("NEEDS_OWNER", Assert.Single(byStaff.Skipped).Reason);

        var noOverride = await Roll(e, x.Dx, Request(x.Td, [x.A, x.RolledOnce]));
        Assert.Equal([x.A], noOverride.RolledOver.Select(r => r.EntryId).ToList());
        Assert.Equal(("EXCEEDS_TOTAL_EXTENSION", x.RolledOnce), (Assert.Single(noOverride.Skipped).Reason, noOverride.Skipped[0].EntryId));

        var withOverride = await Roll(e, x.Dx, Request(x.Td, [x.RolledOnce], overrideLimit: true));
        Assert.Equal([x.RolledOnce], withOverride.RolledOver.Select(r => r.EntryId).ToList());
        Assert.Equal((TdDue, 2), ((await EntriesAsync(e))[x.RolledOnce].DueDate, (await EntriesAsync(e))[x.RolledOnce].RolloverCount));
    }

    [CreditDbFact]
    public async Task The_seasons_must_allow_the_rollover()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);

        await Assert.ThrowsAsync<NotFoundException>(() => Roll(e, Guid.NewGuid(), Request()));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Roll(e, x.Dx, Request(x.Dx)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Roll(e, x.Ht, Request(x.Dx)));        // earlier season
        await Assert.ThrowsAsync<BusinessRuleException>(() => Roll(e, x.Dx, Request(Guid.NewGuid())));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Roll(e, x.Td, Request()));            // no later season
        await Assert.ThrowsAsync<BusinessRuleException>(() => Run(e, sp => sp.GetRequiredService<IDebtRolloverService>()
            .RolloverSeasonAsync(x.Dx, Request() with { ReasonCode = "TRI_AN" }, Token)));
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var ht = await db.CropSeasons.SingleAsync(c => c.Id == x.Ht, Token);
            ht.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 10, 1));
            await db.SaveChangesAsync(Token);
        }
        await Assert.ThrowsAsync<BusinessRuleException>(() => Roll(e, x.Dx, Request(x.Ht)));
        // The default skips the closed season and takes Thu Đông.
        var result = await Roll(e, x.Dx, Request(ids: [x.A], dryRun: true));
        Assert.Equal((x.Td, TdDue), (result.TargetSeason.Id, result.NewDueDate));
    }

    // Each Farmer is locked in farmerProfileId order; a cash repayment locks one Farmer. Running both at once never deadlocks.
    [CreditDbFact]
    public async Task A_season_rollover_and_a_cash_repayment_at_once_both_complete()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var x = await SetupAsync(e);
        var balance = (await TraceAsync(e)).Balance;

        for (var round = 0; round < 3; round++)
        {
            var rollover = round == 0 ? Roll(e, x.Dx, Request()) : Roll(e, x.Ht, Request(x.Td, overrideLimit: true));
            var payment = Run(e, sp => sp.GetRequiredService<IPaymentService>()
                .ReceiveCashAsync(new("DEBT_REPAYMENT", 100_000m, FarmerProfileId: e.FarmerId), Token));
            var all = Task.WhenAll(rollover, payment);
            Assert.Same(all, await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60), Token)));
            await all;
        }

        Assert.Equal(balance - 300_000m, (await TraceAsync(e)).Balance);
        var entries = await EntriesAsync(e);
        Assert.Equal((TdDue, 2), (entries[x.Other].DueDate, entries[x.Other].RolloverCount));
    }
}
