using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1, needs migration CustomerGroupDefaultCreditTier applied):
// customer_groups.default_credit_tier_id (database design §35.20), always rolled back.
public class CustomerGroupCreditTierDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Unique() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [RealDbFact]
    public async Task A_group_stores_its_default_credit_tier_and_can_remove_it()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (_, store, _) = await session.SeedAsync(context);
        var tier = new CreditTier(store.Id, $"T{Unique()}", "Khách quen", 20_000_000m, 30);
        var group = new CustomerGroup(store.Id, $"G{Unique()}", "Khách quen");
        group.SetDefaultCreditTier(tier.Id);
        context.AddRange(tier, group);
        await context.SaveChangesAsync(Token);

        await using (var reader = session.NewContext())
        {
            var stored = await reader.CustomerGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id, Token);
            Assert.Equal(tier.Id, stored.DefaultCreditTierId);
        }

        group.SetDefaultCreditTier(null);
        await context.SaveChangesAsync(Token);

        await using var secondReader = session.NewContext();
        Assert.Null((await secondReader.CustomerGroups.AsNoTracking().SingleAsync(g => g.Id == group.Id, Token)).DefaultCreditTierId);
    }

    [RealDbFact]
    public async Task An_unknown_credit_tier_is_rejected_by_the_foreign_key()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (_, store, _) = await session.SeedAsync(context);
        var group = new CustomerGroup(store.Id, $"G{Unique()}", "Khách mới");
        group.SetDefaultCreditTier(Guid.NewGuid());
        context.Add(group);

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
        Assert.Equal("fk_customer_groups_default_credit_tier_id", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task A_credit_tier_used_by_a_group_cannot_be_physically_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (_, store, _) = await session.SeedAsync(context);
        var tier = new CreditTier(store.Id, $"T{Unique()}", "Thân thiết", 50_000_000m, 60);
        var group = new CustomerGroup(store.Id, $"G{Unique()}", "Thân thiết");
        group.SetDefaultCreditTier(tier.Id);
        context.AddRange(tier, group);
        await context.SaveChangesAsync(Token);

        var violation = await RealDb.ExpectViolationAsync(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM credit_tiers WHERE id = {tier.Id}", Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
        Assert.Equal("fk_customer_groups_default_credit_tier_id", violation.ConstraintName);
    }
}
