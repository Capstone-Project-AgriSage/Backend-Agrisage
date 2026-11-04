using AgriSage.Api.Extensions;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20: the minimal demo data for the debt flow (a local/test database only).
public sealed class DebtDemoSeederDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("localhost", false, true)]
    [InlineData("LOCALHOST", false, true)]
    [InlineData("127.0.0.1", false, true)]
    [InlineData("::1", false, true)]
    [InlineData("localhost", true, false)]                                       // never in Production
    [InlineData("db.rgbgakdmyoklycepmhmc.supabase.co", false, false)]            // a shared database
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void The_demo_data_goes_only_to_a_local_database_outside_production(string? host, bool production, bool allowed) =>
        Assert.Equal(allowed, MaintenanceCommands.DemoDataAllowed(host, production));

    private static async Task<DebtDemoSeeder.Result> SeedAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        if (!await db.Roles.AnyAsync(r => r.Code == RoleCode.SalesStaff, Token))
        {
            db.Roles.Add(new Role(RoleCode.SalesStaff, "Sales Staff"));
            await db.SaveChangesAsync(Token);
        }

        return await new DebtDemoSeeder(db, new PasswordHashService(), e.Clock).SeedAsync("Demo-pass-123", Token);
    }

    [CreditDbFact]
    public async Task The_demo_set_has_normal_overdue_disputed_and_rolled_over_debts_and_sends_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var result = await SeedAsync(e);
        Assert.Equal(new DebtDemoSeeder.Result(false, 2, 2, 4, 4), result);

        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var today = AgriSage.Application.Common.BusinessCalendar.Today(e.Clock.UtcNow);
        var seasons = await db.CropSeasons.AsNoTracking().Where(c => c.Code.StartsWith("DEMO")).OrderBy(c => c.Code).ToListAsync(Token);
        Assert.Equal(["Collecting", "Open"], seasons.Select(c => c.PhaseOn(today).ToString()));
        var a = seasons[0];
        var b = seasons[1];
        Assert.Equal(a.HarvestStart, b.OrderWindowStart);                              // consecutive, no gap

        var entries = await db.DebtEntries.AsNoTracking().Where(x => x.EntryNumber.StartsWith("DEMO-")).OrderBy(x => x.EntryNumber).ToListAsync(Token);
        Assert.Equal(4, entries.Count);
        Assert.Equal((b.Id, 0, false), (entries[0].CollectionCropSeasonId, entries[0].RolloverCount, entries[0].DueDate < today));       // normal, not due
        Assert.True(entries[1].DueDate < today && entries[1].CollectionCropSeasonId == a.Id);                                           // overdue
        Assert.Equal(DebtEntryStatus.Disputed, entries[2].Status);                                                                      // disputed
        Assert.Equal((a.Id, b.Id, 1), (entries[3].OriginCropSeasonId, entries[3].CollectionCropSeasonId, entries[3].RolloverCount));   // rolled over A -> B
        Assert.Equal(a.PaymentDueDate, entries[3].OriginalDueDate);
        Assert.Equal(14_000_000m, entries.Sum(x => x.OutstandingAmount));
        Assert.Equal(14_000_000m, await db.DebtAccounts.AsNoTracking().SumAsync(x => x.CurrentBalance, Token) - 0m);

        // Reminders are not started and nothing is sent.
        Assert.Null((await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(Token))?.RemindersStartDate);
        Assert.Equal(0, await db.Notifications.IgnoreQueryFilters().CountAsync(Token));
        Assert.DoesNotContain(await db.Users.AsNoTracking().Select(u => u.PasswordHash).ToListAsync(Token), h => h.Contains("Demo-pass-123"));
    }

    [CreditDbFact]
    public async Task Running_the_demo_seed_again_changes_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await SeedAsync(e);
        int Count(AgriSageDbContext db, out int users, out int debts, out int seasons)
        {
            users = db.Users.IgnoreQueryFilters().Count();
            debts = db.DebtEntries.IgnoreQueryFilters().Count();
            seasons = db.CropSeasons.IgnoreQueryFilters().Count();
            return users + debts + seasons;
        }

        int users1, debts1, seasons1;
        using (var s = e.Scope()) Count(s.ServiceProvider.GetRequiredService<AgriSageDbContext>(), out users1, out debts1, out seasons1);
        var again = await SeedAsync(e);
        Assert.True(again.AlreadySeeded);
        using var s2 = e.Scope();
        Count(s2.ServiceProvider.GetRequiredService<AgriSageDbContext>(), out var users2, out var debts2, out var seasons2);
        Assert.Equal((users1, debts1, seasons1), (users2, debts2, seasons2));
    }
}
