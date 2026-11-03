using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
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

// F3.20 review finding 1: "apply the new due date to the debts being collected" when a season's dates change. It runs under the
// store lock, locks every Farmer concerned (in id order), reads the entries AFTER the locks, and leaves disputed entries alone.
public sealed class CropSeasonExtendDebtDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly OldDue = new(2027, 4, 14);    // 15/03/2027 + 30 days
    private static readonly DateOnly NewDue = new(2027, 5, 14);    // 15/03/2027 + 60 days

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static async Task<(Guid Dx, long Version)> SeasonsAsync(CreditTestDatabase e, bool withNext = false)
    {
        var dx = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
            new("DX-2026", "Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30, new DateOnly(2026, 10, 1)), Token));
        if (withNext)
            await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("HT-2027", "Hè Thu", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30), Token));
        var current = await Run(e, sp => sp.GetRequiredService<ICropSeasonService>().GetAsync(dx.Id, Token));
        return (dx.Id, current.Version);
    }

    private static Task<CropSeasonResponse> ExtendAsync(CreditTestDatabase e, Guid dx, long version) =>
        Run(e, sp => sp.GetRequiredService<ICropSeasonService>().UpdateAsync(dx,
            new("Đông Xuân", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 60, version,
                ApplyNewDueDateToCollectingDebts: true, Reason: "Lũ làm trễ thu hoạch"), Token));

    private static async Task<Guid> DebtAsync(CreditTestDatabase e, Guid farmer, Guid season, decimal amount = 1_000_000m) =>
        (await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(amount, null, "Nợ vụ", season), Token))).Id;

    private static async Task<Guid> SecondFarmerAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
        var user = new User(role.Id, "Farmer 2", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new FarmerProfile(user.Id);
        db.AddRange(user, farmer, new FarmerCreditProfile(e.StoreId, farmer.Id, 20_000_000m, e.User.UserId!.Value, e.Clock.UtcNow, e.TierId),
            new DebtAccount(e.StoreId, farmer.Id));
        await db.SaveChangesAsync(Token);
        return farmer.Id;
    }

    private static async Task<(DateOnly Due, string Status)> StateAsync(CreditTestDatabase e, Guid entry)
    {
        using var s = e.Scope();
        var row = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.Id == entry, Token);
        return (row.DueDate, row.Status.ToString());
    }

    // Disputed entries wait for the owner's decision: the season's new date does not move them. Everything else that is still
    // owed and on the old date moves, with one action each.
    [CreditDbFact]
    public async Task A_disputed_entry_keeps_its_due_date_and_the_others_move()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, version) = await SeasonsAsync(e);
        var open = await DebtAsync(e, e.FarmerId, dx);
        var disputed = await DebtAsync(e, e.FarmerId, dx);
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(disputed, "DISPUTE", "Khách không đồng ý", null, null, null, Token));
        Assert.Equal((OldDue, "Disputed"), await StateAsync(e, disputed));

        await ExtendAsync(e, dx, version);
        Assert.Equal(NewDue, (await StateAsync(e, open)).Due);
        Assert.Equal((OldDue, "Disputed"), await StateAsync(e, disputed));
        using var s = e.Scope();
        var actions = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntryActions.AsNoTracking()
            .Where(a => a.ActionType == DebtEntryActionType.ChangeDueDate).Select(a => a.DebtEntryId).ToListAsync(Token);
        Assert.Equal([open], actions);
    }

    // Deterministic: while another transaction holds the Farmer lock (a payment or a dispute that has not committed), the season
    // update waits; afterwards it works from what was committed — the paid and the disputed entries are not moved.
    [CreditDbFact]
    public async Task The_season_update_waits_for_the_farmer_lock_and_then_sees_what_was_committed()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, version) = await SeasonsAsync(e);
        var stays = await DebtAsync(e, e.FarmerId, dx);
        var paid = await DebtAsync(e, e.FarmerId, dx);
        var disputed = await DebtAsync(e, e.FarmerId, dx);
        var other = await SecondFarmerAsync(e);
        var otherEntry = await DebtAsync(e, other, dx);

        using var holder = e.Scope();
        var db = holder.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await holder.ServiceProvider.GetRequiredService<IRowLockService>().LockFarmerProfileAsync(e.FarmerId, Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET outstanding_amount = 0, status = 'PAID' WHERE id = {paid}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET status = 'DISPUTED' WHERE id = {disputed}", Token);

        var update = Task.Run(() => ExtendAsync(e, dx, version), Token);
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(update.IsCompleted, "the season update did not wait for the Farmer lock");
        await tx.CommitAsync(Token);
        await update.WaitAsync(TimeSpan.FromSeconds(30), Token);

        Assert.Equal(NewDue, (await StateAsync(e, stays)).Due);
        Assert.Equal(NewDue, (await StateAsync(e, otherEntry)).Due);                   // another Farmer's entry moves too
        Assert.Equal(OldDue, (await StateAsync(e, paid)).Due);                         // paid while the update waited
        Assert.Equal(OldDue, (await StateAsync(e, disputed)).Due);                     // disputed while the update waited
    }

    // A payment, a rollover, a dispute and the season update on the same customers at once: no deadlock (40P01 would surface as
    // an unexpected exception), and every entry's due date agrees with its own history.
    [CreditDbFact]
    public async Task A_payment_a_rollover_a_dispute_and_the_season_update_together_end_consistent_without_deadlock()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var (dx, version) = await SeasonsAsync(e, withNext: true);
        var other = await SecondFarmerAsync(e);
        var ids = new List<Guid>();
        foreach (var farmer in new[] { e.FarmerId, other })
            for (var i = 0; i < 4; i++) ids.Add(await DebtAsync(e, farmer, dx));
        Guid ht;
        using (var s = e.Scope())
            ht = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().CropSeasons.AsNoTracking().SingleAsync(c => c.Code == "HT-2027", Token)).Id;

        var work = new[]
        {
            Attempt(() => ExtendAsync(e, dx, version)),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", 1_500_000m, FarmerProfileId: e.FarmerId), Token))),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(ids[1], new("MAT_MUA", "Mất mùa", ht), Token))),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IDebtRolloverService>().RolloverAsync(ids[5], new("MAT_MUA", "Mất mùa", ht), Token))),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(ids[2], "DISPUTE", "Khiếu nại", null, null, null, Token))),
            Attempt(() => Run(e, sp => sp.GetRequiredService<IDebtService>().ActionAsync(ids[6], "DISPUTE", "Khiếu nại", null, null, null, Token))),
        };
        await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(60), Token);

        using var verify = e.Scope();
        var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var entries = await db.DebtEntries.AsNoTracking().Include(x => x.Actions).Where(x => ids.Contains(x.Id)).ToListAsync(Token);
        foreach (var entry in entries)
        {
            var lastMove = entry.Actions.Where(a => a.NewDueDate != null).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).LastOrDefault();
            Assert.Equal(lastMove?.NewDueDate ?? OldDue, entry.DueDate);
            Assert.Equal(OldDue, entry.OriginalDueDate);
            Assert.InRange(entry.OutstandingAmount, 0m, 1_000_000m);
        }

        var balance = await db.DebtAccounts.AsNoTracking().SumAsync(a => a.CurrentBalance, Token);
        Assert.Equal(await db.DebtEntries.AsNoTracking().SumAsync(x => x.OutstandingAmount, Token), balance);
        Assert.Equal(balance, await db.DebtTransactions.AsNoTracking().Where(t => t.Status == DebtTransactionStatus.Posted).SumAsync(t => t.AmountDelta, Token));
    }

    // A refusal by a business rule is an outcome of a race (e.g. the entry was already paid); anything else, a deadlock included, fails.
    private static async Task<bool> Attempt(Func<Task> act)
    {
        try { await act(); return true; }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException or ConflictException) { return false; }
    }
}
