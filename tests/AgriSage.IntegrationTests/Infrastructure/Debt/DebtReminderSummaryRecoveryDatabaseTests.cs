using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20 review finding 3: the Farmer reminders are saved page by page and the staff/owner summaries last. A failure in between
// must not lose the summaries for good: the next run finds the Farmer reminders already saved (so sends none again) and writes
// the missing summaries (the per-day key keeps them to one). The summaries say "N nhóm nợ" — they count debt groups.
public sealed class DebtReminderSummaryRecoveryDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Fails the save that writes a staff/owner summary — after the Farmer reminders of every page were saved.
    private sealed class FailOnSummary : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<Notification>().Any(entry => entry.State == EntityState.Added
                    && entry.Entity.DeduplicationKey!.StartsWith("debt-reminder-staff", StringComparison.Ordinal)))
                throw new InvalidOperationException("injected failure before the summaries");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IServiceProvider, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider);
    }

    private static void At(CreditTestDatabase e, int year, int month, int day) =>
        e.Clock.UtcNow = new DateTimeOffset(year, month, day, 5, 0, 0, TimeSpan.Zero);

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

    private static async Task<List<(string Key, string Message)>> NoticesAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.DeduplicationKey != null && n.DeduplicationKey.StartsWith("debt-reminder"))
                .Select(n => new { n.DeduplicationKey, n.Message }).ToListAsync(Token))
            .Select(n => (n.DeduplicationKey!, n.Message)).OrderBy(n => n.Item1, StringComparer.Ordinal).ToList();
    }

    private static async Task RunFailingAsync(CreditTestDatabase e, int batch)
    {
        using var scope = e.Scope();
        var p = scope.ServiceProvider;
        await using var context = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(e.ConnectionString)
            .AddInterceptors(new SoftDeleteInterceptor(e.User, e.Clock), new AuditableEntityInterceptor(e.Clock),
                new ConcurrencyVersionInterceptor(), new FailOnSummary()).Options);
        var service = new OperationalAlertsService(context, new NotificationWriter(context), p.GetRequiredService<IInventoryService>(), e.Clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DebtRemindersAsync(batch, Token));
    }

    private static Task RunAsync(CreditTestDatabase e) =>
        Run(e, async sp => { await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(1, Token); return 0; });

    private static async Task<List<(string Key, string Message, Guid User)>> SummariesAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.DeduplicationKey != null && n.DeduplicationKey.StartsWith("debt-reminder-staff:"))
                .Select(n => new { n.DeduplicationKey, n.Message, n.UserId }).ToListAsync(Token))
            .Select(n => (n.DeduplicationKey!, n.Message, n.UserId)).OrderBy(n => n.Item1, StringComparer.Ordinal).ToList();
    }

    // The failed run was on day D; the next run is on D+1. The summary of D is recovered once, with D's numbers and for the members
    // who were there on D; D+1 has its own summary with its own numbers; nothing is written twice.
    [CreditDbFact]
    public async Task A_summary_lost_on_day_D_is_recovered_on_day_D_plus_1_with_the_numbers_of_D()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var other = await SecondFarmerAsync(e);
        using (var s = e.Scope())
            await ReminderStartHelper.EnsureAsync(s.ServiceProvider.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token);

        At(e, 2026, 11, 4);                                                                  // day D: two groups reach N3
        foreach (var farmer in new[] { e.FarmerId, other })
            await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(500_000m, new DateOnly(2026, 11, 4), "Nợ"), Token));
        await RunFailingAsync(e, batch: 1);
        Assert.Empty(await SummariesAsync(e));
        var farmerNotices = await NoticesAsync(e);
        Assert.Equal(2, farmerNotices.Count);

        At(e, 2026, 11, 5);                                                                  // day D+1: one new group, and a new member
        await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(300_000m, new DateOnly(2026, 11, 5), "Nợ"), Token));
        Guid newStaff;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var sale = await db.Roles.SingleOrDefaultAsync(r => r.Code == RoleCode.SalesStaff, Token) ?? new Role(RoleCode.SalesStaff, "Sale");
            var user = new User(sale.Id, "Staff", "hash", $"{Guid.NewGuid():N}@example.test", null);
            if (db.Entry(sale).State == EntityState.Detached) db.Add(sale);
            db.AddRange(user, new StoreMember(e.StoreId, user.Id));
            await db.SaveChangesAsync(Token);
            newStaff = user.Id;
        }

        await RunAsync(e);
        var summaries = await SummariesAsync(e);
        var forD = summaries.Where(n => n.Key == "debt-reminder-staff:20261104:N3").ToList();
        var forD1 = summaries.Where(n => n.Key == "debt-reminder-staff:20261105:N3").ToList();
        Assert.NotEmpty(forD);
        Assert.All(forD, n => Assert.StartsWith("Ngày 04/11/2026: 2 nhóm nợ", n.Message));         // D's numbers, dated, not 3
        Assert.DoesNotContain(forD, n => n.User == newStaff);                                  // the member who joined on D+1 was not there on D
        Assert.NotEmpty(forD1);
        Assert.All(forD1, n => Assert.StartsWith("1 nhóm nợ", n.Message));                     // D+1's own number, not 3
        Assert.Contains(forD1, n => n.User == newStaff);
        Assert.Equal(2, summaries.Select(n => n.Key).Distinct().Count());

        // The Farmer reminders of D are untouched (plus D+1's one), and another run adds nothing.
        Assert.Equal(farmerNotices, (await NoticesAsync(e)).Where(n => n.Key.StartsWith("debt-reminder:", StringComparison.Ordinal))
            .Where(n => farmerNotices.Any(f => f.Key == n.Key)).ToList());
        var before = await NoticesAsync(e);
        await RunAsync(e);
        Assert.Equal(before, await NoticesAsync(e));
        Assert.Equal(summaries.Count, (await SummariesAsync(e)).Count);
    }

    [CreditDbFact]
    public async Task A_run_that_failed_before_the_summaries_is_repaired_by_the_next_run_without_sending_anything_twice()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        At(e, 2026, 11, 4);
        var due = new DateOnly(2026, 11, 4);                                               // N3 today for both customers
        var other = await SecondFarmerAsync(e);
        foreach (var farmer in new[] { e.FarmerId, other })
            await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(500_000m, due, "Nợ"), Token));
        using (var s = e.Scope())
            await ReminderStartHelper.EnsureAsync(s.ServiceProvider.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token);

        // Run 1: a context whose save fails when the summaries are written; the Farmer reminders (one page each) are already saved.
        var failing = new FailOnSummary();
        using (var scope = e.Scope())
        {
            var p = scope.ServiceProvider;
            await using var context = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(e.ConnectionString)
                .AddInterceptors(new SoftDeleteInterceptor(e.User, e.Clock), new AuditableEntityInterceptor(e.Clock),
                    new ConcurrencyVersionInterceptor(), failing).Options);
            var service = new OperationalAlertsService(context, new NotificationWriter(context), p.GetRequiredService<IInventoryService>(), e.Clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DebtRemindersAsync(1, Token));
        }

        var afterFailure = await NoticesAsync(e);
        Assert.Equal(2, afterFailure.Count);                                                // the two Farmer reminders, no summary
        Assert.All(afterFailure, n => Assert.StartsWith("debt-reminder:", n.Key));

        // Run 2 (normal): no Farmer reminder again, the missing summary is written once, and says how many debt groups.
        await Run(e, async sp => { await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(1, Token); return 0; });
        var repaired = await NoticesAsync(e);
        Assert.Equal(afterFailure, repaired.Where(n => n.Key.StartsWith("debt-reminder:", StringComparison.Ordinal)).ToList());
        var summaries = repaired.Where(n => n.Key.StartsWith("debt-reminder-staff:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(summaries);
        Assert.All(summaries, n => Assert.Equal("debt-reminder-staff:20261104:N3", n.Key));
        Assert.All(summaries, n => Assert.Contains("2 nhóm nợ", n.Message));
        Assert.DoesNotContain(summaries, n => n.Message.Contains("khách", StringComparison.OrdinalIgnoreCase));

        // Run 3: nothing new.
        await Run(e, async sp => { await sp.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(1, Token); return 0; });
        Assert.Equal(repaired, await NoticesAsync(e));
    }
}
