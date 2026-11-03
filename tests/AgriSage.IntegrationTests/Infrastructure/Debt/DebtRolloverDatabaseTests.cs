using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.14 against real PostgreSQL: extending one debt entry to a later season (CS-07, CS-08, D5, D9) and the reason codes of
// the other debt actions (CS-11, D7). The fixture's today is 05/10/2026 (Vietnam); a pickup debt is due on 04/11/2026
// (30-day tier term, no season yet).
public sealed class DebtRolloverDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly CreateCropSeasonRequest DongXuan =
        new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1));
    private static readonly CreateCropSeasonRequest HeThu =
        new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);
    private static readonly CreateCropSeasonRequest ThuDong =
        new("TD-2027", "Thu Đông 2027", new DateOnly(2027, 11, 15), new DateOnly(2027, 12, 15), 30);

    private static readonly DateOnly TermDue = new(2026, 11, 4);
    private static readonly DateOnly DongXuanDue = new(2027, 4, 14);
    private static readonly DateOnly HeThuDue = new(2027, 9, 14);
    private static readonly DateOnly ThuDongDue = new(2028, 1, 14);

    private static DebtRolloverRequest Request(Guid? target = null, string code = "MAT_MUA", bool overrideLimit = false) =>
        new(code, "Sâu cuốn lá, mất mùa", target, overrideLimit);

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static Task<DebtEntryResponse> Rollover(CreditTestDatabase e, Guid entryId, DebtRolloverRequest request) =>
        Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(entryId, request, Token));

    private static Task<CropSeasonResponse> Season(CreditTestDatabase e, CreateCropSeasonRequest request) =>
        Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(request, Token));

    private static async Task<Guid> DebtAsync(CreditTestDatabase e, long quantity = 5)
    {
        var order = await e.FulfillAsync(quantity, Token, withoutSeason: true);
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .SingleAsync(x => x.OrderId == order.Id, Token)).Id;
    }

    private static async Task<DebtEntry> EntryAsync(CreditTestDatabase e, Guid id)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking()
            .Include(x => x.Actions).SingleAsync(x => x.Id == id, Token);
    }

    private static async Task<(decimal Balance, int Ledger)> LedgerAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return ((await db.DebtAccounts.AsNoTracking().SingleAsync(Token)).CurrentBalance, await db.DebtTransactions.CountAsync(Token));
    }

    // A Sale member holding DEBT.ROLLOVER through a member override (its role has no defaults in the fixture).
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

    private static async Task SetPolicyAsync(CreditTestDatabase e, int staffLimit, int ownerReview)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var policy = new StoreDebtPolicy(e.StoreId);
        policy.Update(30, DebtReminderOffsets.Default, staffLimit, ownerReview, 2, null, 365);
        db.StoreDebtPolicies.Add(policy);
        await db.SaveChangesAsync(Token);
    }

    [CreditDbFact]
    public async Task Staff_extend_once_to_the_next_season_and_only_the_season_and_due_date_move()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await Season(e, DongXuan);
        var ht = await Season(e, HeThu);
        var id = await DebtAsync(e);
        var before = await LedgerAsync(e);
        var staff = await RolloverStaffAsync(e);

        DebtEntryResponse response = null!;
        await As(e, staff, "SALES_STAFF", async () => response = await Rollover(e, id, Request()));

        // No target: the next season whose due date is later than the current one.
        var entry = await EntryAsync(e, id);
        Assert.Equal((DongXuanDue, dx.Id, (Guid?)null, TermDue, 1, 5_000_000m, DebtEntryStatus.Open),
            (entry.DueDate, entry.CollectionCropSeasonId!.Value, entry.OriginCropSeasonId, entry.OriginalDueDate, entry.RolloverCount,
                entry.OutstandingAmount, entry.Status));
        Assert.Equal(before, await LedgerAsync(e));
        var action = Assert.Single(entry.Actions);
        Assert.Equal((DebtEntryActionType.Rollover, TermDue, DongXuanDue, (Guid?)null, dx.Id, DebtReasonCode.MatMua, "Sâu cuốn lá, mất mùa", staff),
            (action.ActionType, action.OldDueDate!.Value, action.NewDueDate!.Value, action.OldCropSeasonId, action.NewCropSeasonId!.Value,
                action.ReasonCode!.Value, action.Reason, action.CreatedBy));
        var shown = Assert.Single(response.Actions);
        Assert.Equal(("ROLLOVER", "MAT_MUA", false, dx.Id), (shown.ActionType, shown.ReasonCode, shown.IsPrePolicy, shown.NewCropSeasonId!.Value));
        Assert.Equal(DongXuanDue, response.DueDate);

        // CS-08: from the second rollover the owner decides.
        await As(e, staff, "SALES_STAFF", () => Assert.ThrowsAsync<ForbiddenException>(() => Rollover(e, id, Request(ht.Id))));
        Assert.Equal(1, (await EntryAsync(e, id)).RolloverCount);
        await Rollover(e, id, Request(ht.Id, "THIEN_TAI"));
        entry = await EntryAsync(e, id);
        Assert.Equal((HeThuDue, ht.Id, TermDue, 2), (entry.DueDate, entry.CollectionCropSeasonId!.Value, entry.OriginalDueDate, entry.RolloverCount));
        Assert.Equal(before, await LedgerAsync(e));

        using var verify = e.Scope();
        var audits = await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.Action == "DEBT_ROLLOVER" && a.EntityId == id).ToListAsync(Token);
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal(("DEBT_ENTRY", e.StoreId), (a.EntityType, a.StoreId!.Value)));
        var first = audits.Single(a => a.ActorUserId == staff);
        var values = JsonDocument.Parse(first.NewValues!).RootElement;
        Assert.Equal(("MAT_MUA", dx.Id, "2027-04-14", false), (values.GetProperty("reasonCode").GetString(),
            values.GetProperty("collectionCropSeasonId").GetGuid(), values.GetProperty("dueDate").GetString(),
            values.GetProperty("overrideExtensionLimit").GetBoolean()));
    }

    [CreditDbFact]
    public async Task The_store_policy_sets_how_often_staff_may_extend()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await SetPolicyAsync(e, staffLimit: 0, ownerReview: 1);
        await Season(e, DongXuan);
        var id = await DebtAsync(e);
        var staff = await RolloverStaffAsync(e);

        await As(e, staff, "SALES_STAFF", () => Assert.ThrowsAsync<ForbiddenException>(() => Rollover(e, id, Request())));
        Assert.Equal(0, (await EntryAsync(e, id)).RolloverCount);
    }

    [CreditDbFact]
    public async Task Staff_without_the_permission_cannot_extend()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Season(e, DongXuan);
        var id = await DebtAsync(e);
        var staff = await RolloverStaffAsync(e, grant: false);

        await As(e, staff, "SALES_STAFF", () => Assert.ThrowsAsync<ForbiddenException>(() => Rollover(e, id, Request())));
        await As(e, e.FarmerUserId, "FARMER", () => Assert.ThrowsAsync<ForbiddenException>(() => Rollover(e, id, Request())));
        Assert.Empty((await EntryAsync(e, id)).Actions);
    }

    // D5: the total extension is counted from the ORIGINAL due date. Staff stay within it; the owner may go beyond it, but
    // only by saying so.
    [CreditDbFact]
    public async Task Going_past_the_total_extension_limit_needs_the_owner_and_an_explicit_override()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await SetPolicyAsync(e, staffLimit: 5, ownerReview: 5);
        var dx = await Season(e, DongXuan);
        var ht = await Season(e, HeThu);
        var td = await Season(e, ThuDong);
        var id = await DebtAsync(e);
        var staff = await RolloverStaffAsync(e);

        await As(e, staff, "SALES_STAFF", async () =>
        {
            await Rollover(e, id, Request(dx.Id));
            await Rollover(e, id, Request(ht.Id)); // 314 days after 04/11/2026: within 365
            // 14/01/2028 is 436 days after the original due date.
            await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(td.Id)));
            await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(td.Id, overrideLimit: true)));
        });
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(td.Id)));
        Assert.Equal((HeThuDue, 2), ((await EntryAsync(e, id)).DueDate, (await EntryAsync(e, id)).RolloverCount));

        await Rollover(e, id, Request(td.Id, overrideLimit: true));
        var entry = await EntryAsync(e, id);
        Assert.Equal((ThuDongDue, td.Id, TermDue, 3), (entry.DueDate, entry.CollectionCropSeasonId!.Value, entry.OriginalDueDate, entry.RolloverCount));
        using var verify = e.Scope();
        var audit = await verify.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.Action == "DEBT_ROLLOVER" && a.EntityId == id && a.ActorUserId == e.User.UserId).SingleAsync(Token);
        Assert.True(JsonDocument.Parse(audit.NewValues!).RootElement.GetProperty("overrideExtensionLimit").GetBoolean());
    }

    [CreditDbFact]
    public async Task The_target_season_and_the_entry_must_allow_a_rollover()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var id = await DebtAsync(e);

        // No season after the current due date yet.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request()));
        var dx = await Season(e, DongXuan);
        var ht = await Season(e, HeThu);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(Guid.NewGuid())));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(code: "TRI_AN")));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(code: "BOGUS")));

        await Rollover(e, id, Request(dx.Id));
        // The same season again, or an earlier due date, is not a rollover.
        await Assert.ThrowsAsync<DomainException>(() => Rollover(e, id, Request(dx.Id)));

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var season = await db.CropSeasons.SingleAsync(x => x.Id == ht.Id, Token);
            season.Close(e.User.UserId!.Value, e.Clock.UtcNow, "{}", new DateOnly(2027, 10, 1));
            await db.SaveChangesAsync(Token);
        }
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request(ht.Id)));
        // With the next season closed there is no later season to default to.
        await Assert.ThrowsAsync<BusinessRuleException>(() => Rollover(e, id, Request()));
        Assert.Equal((DongXuanDue, 1), ((await EntryAsync(e, id)).DueDate, (await EntryAsync(e, id)).RolloverCount));
    }

    [CreditDbFact]
    public async Task Disputed_paid_and_unknown_entries_cannot_be_extended()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Season(e, DongXuan);
        var disputed = await DebtAsync(e, 2);
        var cancelled = await DebtAsync(e, 3);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "DISPUTE", "Sai số lượng", null, null, Token));
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(cancelled, "CANCEL", "Miễn nợ", null, null, Token));

        await Assert.ThrowsAsync<DomainException>(() => Rollover(e, disputed, Request()));
        await Assert.ThrowsAsync<DomainException>(() => Rollover(e, cancelled, Request()));
        await Assert.ThrowsAsync<NotFoundException>(() => Rollover(e, Guid.NewGuid(), Request()));
        Assert.Equal(0, (await EntryAsync(e, disputed)).RolloverCount);
    }

    [CreditDbFact]
    public async Task Two_rollovers_at_once_extend_the_entry_only_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Season(e, DongXuan);
        var id = await DebtAsync(e);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await Rollover(e, id, Request()); return true; }
            catch (DomainException) { return false; }
            catch (BusinessRuleException) { return false; }
        }));

        Assert.Single(results, ok => ok);
        var entry = await EntryAsync(e, id);
        Assert.Equal((1, DongXuanDue), (entry.RolloverCount, entry.DueDate));
        Assert.Single(entry.Actions);
    }

    // The Farmer is told about a new due date once, through the audit outbox.
    [CreditDbFact]
    public async Task A_rollover_notifies_the_farmer_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Season(e, DongXuan);
        var id = await DebtAsync(e);
        await Rollover(e, id, Request());

        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var log = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "DEBT_ROLLOVER", Token);
        var outbox = await db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.AuditLogId == log.Id, Token);
        var dispatcher = new NotificationDispatcher(db, new NotificationOutboxLock(db), new NotificationWriter(db), e.Clock);
        await dispatcher.DispatchAsync(outbox.Id, Token);
        await dispatcher.DispatchAsync(outbox.Id, Token);
        var notice = await db.Notifications.AsNoTracking().SingleAsync(n => n.DeduplicationKey == $"audit:{log.Id:N}", Token);
        Assert.Equal((e.FarmerUserId, "DEBT_DUE_DATE_CHANGED"), (notice.UserId, notice.NotificationType));
    }

    // D7: the reason code is stored with the action. Actions without one show isPrePolicy until it becomes mandatory.
    [CreditDbFact]
    public async Task Owner_actions_store_the_reason_code_and_actions_without_one_show_as_pre_policy()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var adjusted = await DebtAsync(e, 2);
        var cancelled = await DebtAsync(e, 3);
        var moved = await DebtAsync(e, 4);
        var legacy = await DebtAsync(e, 1);
        Task<DebtEntryResponse> Act(Guid id, string action, decimal? amount, DateOnly? due, string? code) =>
            Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(id, action, "Ghi chú của chủ", amount, due, code, Token));

        var adjust = await Act(adjusted, "ADJUST", 500_000m, null, "TRI_AN");
        var cancel = await Act(cancelled, "CANCEL", null, null, "KHONG_THU_HOI");
        var change = await Act(moved, "CHANGE_DUE_DATE", null, new DateOnly(2026, 12, 1), "SAI_SOT");
        var old = await Act(legacy, "CHANGE_DUE_DATE", null, new DateOnly(2026, 12, 1), null);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Act(legacy, "ADJUST", 1_000m, null, "BOGUS"));

        Assert.Equal(("TRI_AN", false), (Assert.Single(adjust.Actions).ReasonCode, adjust.Actions[0].IsPrePolicy));
        Assert.Equal(("KHONG_THU_HOI", false), (Assert.Single(cancel.Actions).ReasonCode, cancel.Actions[0].IsPrePolicy));
        Assert.Equal(("SAI_SOT", false), (Assert.Single(change.Actions).ReasonCode, change.Actions[0].IsPrePolicy));
        Assert.Equal(((string?)null, true), (Assert.Single(old.Actions).ReasonCode, old.Actions[0].IsPrePolicy));
        Assert.Equal(DebtReasonCode.TriAn, (await EntryAsync(e, adjusted)).Actions.Single().ReasonCode);

        // A dispute is the Farmer's, not a debt change: it is never "pre-policy".
        var dispute = await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(legacy, "DISPUTE", "Sai", null, null, Token));
        Assert.False(dispute.Actions.Single(a => a.ActionType == "DISPUTE").IsPrePolicy);
    }

    // §1.1: changing the due date directly never goes to the past and does not count as a rollover; going past the total
    // extension limit is recorded.
    [CreditDbFact]
    public async Task A_direct_due_date_change_is_not_a_rollover_and_records_a_limit_overrun()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var id = await DebtAsync(e);
        Task<DebtEntryResponse> Change(DateOnly due) =>
            Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(id, "CHANGE_DUE_DATE", "Khách xin thêm", null, due, "KHO_KHAN", Token));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Change(new DateOnly(2026, 10, 4)));
        await Change(new DateOnly(2027, 12, 1));
        var entry = await EntryAsync(e, id);
        Assert.Equal((new DateOnly(2027, 12, 1), TermDue, 0), (entry.DueDate, entry.OriginalDueDate, entry.RolloverCount));

        using var s = e.Scope();
        var audit = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == "DEBT_CHANGE_DUE_DATE" && a.EntityId == id, Token);
        var values = JsonDocument.Parse(audit.NewValues!).RootElement;
        Assert.Equal(("KHO_KHAN", true), (values.GetProperty("reasonCode").GetString(), values.GetProperty("exceedsTotalExtension").GetBoolean()));
    }
}
