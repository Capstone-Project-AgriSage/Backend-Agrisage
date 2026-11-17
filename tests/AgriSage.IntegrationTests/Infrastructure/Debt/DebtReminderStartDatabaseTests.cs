using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20 on real PostgreSQL: starting debt reminders (T0). Until the owner starts them the job sends nothing; milestones before
// T0 are never sent (not as a backlog, and not for a debt entered or given a season afterwards); the start is set once, in one
// transaction with its audit, and is safe against two requests at once. Default policy offsets: N2 −7, N3 0, N4 +14, N5 +30.
public sealed class DebtReminderStartDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string StartedAudit = "DEBT_REMINDERS_STARTED";

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    // Vietnam noon of the day.
    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

    private static Task<DebtReminderStartResponse> StartAsync(CreditTestDatabase e, bool dryRun, string? reason = null) =>
        Run(e, sp => sp.GetRequiredService<IDebtReminderStartService>().StartAsync(new(dryRun, reason), Token));

    private static Task RemindAsync(CreditTestDatabase e) =>
        Run(e, async sp => { await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(100, Token); return 0; });

    private static Task<AgriSage.Application.Features.Debt.DebtEntryResponse> DebtAsync(CreditTestDatabase e, decimal amount, DateOnly due, Guid? season = null) =>
        Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(amount, due, "Nợ cũ", season), Token));

    private static async Task<List<string>> ReminderKeysAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.DeduplicationKey != null && n.DeduplicationKey.StartsWith("debt-reminder"))
                .Select(n => n.DeduplicationKey!).ToListAsync(Token)).Order(StringComparer.Ordinal).ToList();
    }

    private static async Task<(int Policies, int Audits, DateOnly? Start, int Notifications)> StateAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return (await db.StoreDebtPolicies.IgnoreQueryFilters().CountAsync(Token),
            await db.AuditLogs.CountAsync(a => a.Action == StartedAudit, Token),
            (await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(Token))?.RemindersStartDate,
            await db.Notifications.IgnoreQueryFilters().CountAsync(Token));
    }

    // The agreed example, started on 03/11: a debt past N5 since October is not reminded late; one whose N5 is on 05/11 still is;
    // and both stay on the debt screens and totals (the start only decides what the job sends).
    [CreditDbFact]
    public async Task Old_milestones_are_skipped_and_later_ones_still_sent_while_the_debts_stay_visible()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));    // N5 on 20/10: past
        await DebtAsync(e, 2_000_000m, new DateOnly(2026, 10, 6));    // N4 on 20/10, N5 on 05/11
        await DebtAsync(e, 500_000m, new DateOnly(2026, 12, 1));      // N2 on 24/11: all ahead

        var started = await StartAsync(e, dryRun: false);
        Assert.Equal((false, false, new DateOnly(2026, 11, 3), 3), (started.DryRun, started.AlreadyStarted, started.StartDate, started.ActiveGroups));
        Assert.Equal((1, 1, 2), (started.Skipped["N5"], started.Skipped["N4"], started.SkippedTotal));
        Assert.Equal(2, started.GroupsWithLaterMilestones);                                       // the 6/10 and the 1/12 groups have milestones ahead
        Assert.Equal(1, started.Farmers);

        await RemindAsync(e);                                                                      // 03/11: nothing, in particular no backlog
        Assert.Empty(await ReminderKeysAsync(e));

        At(e, 2026, 11, 5);
        await RemindAsync(e);                                                                      // N5 of the 6/10 group is after T0
        var keys = await ReminderKeysAsync(e);
        Assert.Equal(2, keys.Count);
        Assert.Contains($"debt-reminder:{e.FarmerId}:20261006:N5", keys);
        Assert.Contains($"debt-reminder-staff:{e.StoreId:N}:20261105:N5", keys);                                 // the owner's summary of that one group
        Assert.DoesNotContain(keys, k => k.Contains("20260920", StringComparison.Ordinal));      // the October-overdue debt is never reminded

        // The skipped debts are untouched and still counted on the screens.
        var dashboard = await Run(e, sp => sp.GetRequiredService<IDebtService>().DashboardAsync(Token));
        Assert.Equal(3_000_000m, dashboard.TotalOverdueDebt);
        Assert.Equal(3_500_000m, dashboard.TotalOutstandingDebt);
    }

    // The job compares dates at every run, so a debt entered after the start with an old due date, or put in a season whose
    // harvest began before T0, cannot bring an old milestone back — whatever the start recorded at the time.
    [CreditDbFact]
    public async Task A_debt_entered_after_the_start_does_not_bring_old_milestones_back()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        CropSeasonResponse season;
        using (var s = e.Scope())
            season = await s.ServiceProvider.GetRequiredService<ICropSeasonService>().CreateAsync(
                new("DX-2026", "Đông Xuân", new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 20), 30, new DateOnly(2026, 8, 1)), Token);
        await StartAsync(e, dryRun: false);

        At(e, 2026, 11, 4);
        await DebtAsync(e, 700_000m, new DateOnly(2026, 9, 1));                        // old: N2..N5 all before T0
        await DebtAsync(e, 800_000m, new DateOnly(2026, 12, 20), season.Id);           // season harvest began 20/10, before T0
        await RemindAsync(e);
        Assert.Empty(await ReminderKeysAsync(e));

        // Control: a debt whose milestones lie ahead of T0 is reminded on its day.
        await DebtAsync(e, 100_000m, new DateOnly(2026, 11, 20));
        At(e, 2026, 11, 20);
        await RemindAsync(e);
        Assert.Contains($"debt-reminder:{e.FarmerId}:20261120:N3", await ReminderKeysAsync(e));
    }

    [CreditDbFact]
    public async Task Without_a_start_date_the_job_sends_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 11, 2));                     // due yesterday
        await RemindAsync(e);
        Assert.Empty(await ReminderKeysAsync(e));
        Assert.Equal(0, (await StateAsync(e)).Notifications);
    }

    // The dry run counts what the real run will skip and writes nothing: no policy row, no audit, no notification.
    [CreditDbFact]
    public async Task The_dry_run_writes_nothing_and_matches_the_real_run()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));
        await DebtAsync(e, 2_000_000m, new DateOnly(2026, 10, 6));
        var before = await StateAsync(e);

        var dry = await StartAsync(e, dryRun: true);
        Assert.True(dry.DryRun);
        Assert.False(dry.AlreadyStarted);
        Assert.Equal(before, await StateAsync(e));
        Assert.Null((await StateAsync(e)).Start);

        var real = await StartAsync(e, dryRun: false);
        Assert.Equal((dry.StartDate, dry.ActiveGroups, dry.SkippedTotal, dry.GroupsWithLaterMilestones),
            (real.StartDate, real.ActiveGroups, real.SkippedTotal, real.GroupsWithLaterMilestones));
        Assert.Equal(dry.Skipped, real.Skipped);
        var after = await StateAsync(e);                                                          // starting sends no notice to anyone
        Assert.Equal((1, 1, new DateOnly(2026, 11, 3), before.Notifications), (after.Policies, after.Audits, after.Start!.Value, after.Notifications));
    }

    // Set once: running it again, even on a later day, keeps T0 and writes nothing more.
    [CreditDbFact]
    public async Task Starting_again_keeps_the_first_date_and_writes_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));
        await StartAsync(e, dryRun: false, "Bắt đầu nhắc nợ");
        var first = await StateAsync(e);
        long version;
        using (var s = e.Scope())
            version = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().StoreDebtPolicies.AsNoTracking().SingleAsync(Token)).Version;

        At(e, 2026, 11, 10);
        var again = await StartAsync(e, dryRun: false);
        Assert.True(again.AlreadyStarted);
        Assert.Equal(new DateOnly(2026, 11, 3), again.StartDate);
        Assert.Equal(first, await StateAsync(e));
        using (var s = e.Scope())
            Assert.Equal(version, (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().StoreDebtPolicies.AsNoTracking().SingleAsync(Token)).Version);

        var dry = await StartAsync(e, dryRun: true);
        Assert.True(dry.AlreadyStarted);
        Assert.Equal(new DateOnly(2026, 11, 3), dry.StartDate);
    }

    // A failure while saving leaves no start date and no policy row: the start and its audit are one transaction.
    [CreditDbFact]
    public async Task A_failed_start_is_rolled_back_completely()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));
        var before = await StateAsync(e);

        // The audit reason is limited to 1000 characters in the database; the validator is not in this path, so the save fails.
        await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(e, dryRun: false, new string('x', 1500)));
        Assert.Equal(before, await StateAsync(e));
        Assert.Null((await StateAsync(e)).Start);

        await RemindAsync(e);                                                          // still not started: the job sends nothing
        Assert.Empty(await ReminderKeysAsync(e));
        Assert.False((await StartAsync(e, dryRun: false)).AlreadyStarted);             // and a clean start works afterwards
    }

    // Deterministic: while another transaction holds the store lock, starts wait for it (they do not each create the policy and
    // the start), then one wins and the others see it already started.
    [CreditDbFact]
    public async Task Starts_wait_for_the_store_lock_and_then_agree_on_one_start()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));

        using var holder = e.Scope();
        var db = holder.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await holder.ServiceProvider.GetRequiredService<AgriSage.Application.Common.Interfaces.IRowLockService>().LockStoreAsync(e.StoreId, Token);

        var starts = new[] { Task.Run(() => StartAsync(e, dryRun: false), Token), Task.Run(() => StartAsync(e, dryRun: false), Token) };
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.All(starts, t => Assert.False(t.IsCompleted, "a start did not wait for the store lock"));
        Assert.Equal(0, (await StateAsync(e)).Policies);

        await tx.CommitAsync(Token);
        var results = await Task.WhenAll(starts);
        Assert.Single(results, r => !r.AlreadyStarted);
        Assert.Equal((1, 1), ((await StateAsync(e)).Policies, (await StateAsync(e)).Audits));
    }

    // Two owners (or one double click) starting at the same moment: one start date, one audit, both answers valid.
    [CreditDbFact]
    public async Task Two_starts_at_once_end_with_one_start_date_and_one_audit()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 3);
        await DebtAsync(e, 1_000_000m, new DateOnly(2026, 9, 20));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => StartAsync(e, dryRun: false), Token)));
        Assert.Single(results, r => !r.AlreadyStarted);
        Assert.All(results, r => Assert.Equal(new DateOnly(2026, 11, 3), r.StartDate));
        var state = await StateAsync(e);
        Assert.Equal((1, 1, new DateOnly(2026, 11, 3)), (state.Policies, state.Audits, state.Start!.Value));
    }
}
