using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Products.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// REAL PostgreSQL (Supabase agrisage-dev after InitialCreate), not mocks. Opt-in: AGRISAGE_DB_TESTS=1.
// Each test works inside a transaction that is rolled back; nothing is committed, no data is left behind.
public class RealDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    // ----- Soft delete, query filters, audit -----

    [RealDbFact]
    public async Task Soft_delete_keeps_the_row_and_the_query_filter_hides_it()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (user, _, _) = await session.SeedAsync(context);
        var brand = new Brand($"Brand {Unique()}");
        context.Add(brand);
        await context.SaveChangesAsync(Token);

        context.Remove(brand);
        await context.SaveChangesAsync(Token);

        await using var reader = session.NewContext();
        Assert.False(await reader.Brands.AnyAsync(b => b.Id == brand.Id, Token));
        var stored = await reader.Brands.IgnoreQueryFilters().SingleAsync(b => b.Id == brand.Id, Token);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(user.Id, stored.DeletedBy);
        Assert.True(stored.UpdatedAt >= stored.CreatedAt);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    // ----- Optimistic concurrency -----

    [RealDbFact]
    public async Task Conflicting_update_of_a_versioned_row_raises_DbUpdateConcurrencyException()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var seeder = session.NewContext();
        var (user, store, farmer) = await session.SeedAsync(seeder);
        var profile = new FarmerCreditProfile(store.Id, farmer.Id, 1_000_000m, user.Id, DateTimeOffset.UtcNow);
        seeder.Add(profile);
        await seeder.SaveChangesAsync(Token);
        Assert.Equal(0, profile.Version);

        await using var first = session.NewContext();
        await using var second = session.NewContext();
        var firstCopy = await first.FarmerCreditProfiles.SingleAsync(p => p.Id == profile.Id, Token);
        var secondCopy = await second.FarmerCreditProfiles.SingleAsync(p => p.Id == profile.Id, Token);

        firstCopy.ChangeStatus(FarmerCreditProfileStatus.Suspended);
        await first.SaveChangesAsync(Token);
        Assert.Equal(1, firstCopy.Version);

        secondCopy.ChangeStatus(FarmerCreditProfileStatus.Suspended);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        await using var reader = session.NewContext();
        Assert.Equal(1, (await reader.FarmerCreditProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id, Token)).Version);
    }

    // ----- Unique / partial unique indexes -----

    [RealDbFact]
    public async Task Active_brand_names_are_unique_but_reusable_after_soft_delete()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var name = $"Brand {Unique()}";
        var first = new Brand(name);
        context.Add(first);
        await context.SaveChangesAsync(Token);

        await using (var duplicate = session.NewContext())
        {
            duplicate.Add(new Brand(name));
            var violation = await RealDb.ExpectViolationAsync(() => duplicate.SaveChangesAsync(Token));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
            Assert.Equal("ux_brands_name", violation.ConstraintName);
        }
    }

    [RealDbFact]
    public async Task Brand_name_can_be_reused_once_the_first_is_soft_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var name = $"Brand {Unique()}";
        var first = new Brand(name);
        context.Add(first);
        await context.SaveChangesAsync(Token);
        context.Remove(first);
        await context.SaveChangesAsync(Token);

        context.Add(new Brand(name));
        await context.SaveChangesAsync(Token);

        Assert.Equal(2, await context.Brands.IgnoreQueryFilters().CountAsync(b => b.Name == name, Token));
    }

    [RealDbFact]
    public async Task User_email_is_unique_case_insensitively()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (existing, _, _) = await session.SeedAsync(context);

        context.Add(new User(existing.RoleId, "Other", "hash", existing.Email!.ToUpperInvariant(), null));
        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
        Assert.Equal("ux_users_email_lower", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Logical_inventory_lot_is_unique_ignoring_case_of_the_lot_number()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var storeProduct = await SeedStoreProductAsync(context);
        var expiry = new DateOnly(2030, 1, 1);
        context.Add(new InventoryLot(storeProduct.Id, "Lot-A", null, expiry));
        await context.SaveChangesAsync(Token);

        await using var duplicate = session.NewContext();
        duplicate.Add(new InventoryLot(storeProduct.Id, "LOT-a", null, expiry));
        var violation = await RealDb.ExpectViolationAsync(() => duplicate.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
        Assert.Equal("ux_inventory_lots_logical_lot", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Logical_inventory_lot_treats_null_expiry_dates_as_equal()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var storeProduct = await SeedStoreProductAsync(context);
        context.Add(new InventoryLot(storeProduct.Id, "Lot-B", null, null));
        await context.SaveChangesAsync(Token);

        await using var duplicate = session.NewContext();
        duplicate.Add(new InventoryLot(storeProduct.Id, "lot-b", null, null));
        var violation = await RealDb.ExpectViolationAsync(() => duplicate.SaveChangesAsync(Token));

        Assert.Equal("ux_inventory_lots_logical_lot", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Only_one_no_lot_bucket_exists_per_store_product()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var storeProduct = await SeedStoreProductAsync(context);
        context.Add(new InventoryLot(storeProduct.Id));
        await context.SaveChangesAsync(Token);

        await using var duplicate = session.NewContext();
        duplicate.Add(new InventoryLot(storeProduct.Id));
        var violation = await RealDb.ExpectViolationAsync(() => duplicate.SaveChangesAsync(Token));

        Assert.Equal("ux_inventory_lots_no_lot_bucket", violation.ConstraintName);
    }

    // ----- CHECK constraints and foreign keys -----

    [RealDbFact]
    public async Task User_must_have_an_email_or_phone_after_trimming()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (existing, _, _) = await session.SeedAsync(context);
        var user = new User(existing.RoleId, "No contact", "hash", $"{Unique()}@example.test", null);
        context.Add(user);
        context.Entry(user).Property(u => u.Email).CurrentValue = "   ";

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        Assert.Equal("ck_users_contact", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Negative_credit_limit_is_rejected_by_a_check_constraint()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (user, store, farmer) = await session.SeedAsync(context);
        var profile = new FarmerCreditProfile(store.Id, farmer.Id, 1m, user.Id, DateTimeOffset.UtcNow);
        context.Add(profile);
        context.Entry(profile).Property(p => p.CreditLimit).CurrentValue = -1m;

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        Assert.Equal("ck_farmer_credit_profiles_credit_limit", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Inventory_balance_with_zero_quantity_must_have_zero_cost()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await session.SeedAsync(context);
        var storeProduct = await SeedStoreProductAsync(context);
        var lot = new InventoryLot(storeProduct.Id, "Lot-C");
        context.Add(lot);
        context.Entry(lot.Balance).Property(b => b.TotalCostValue).CurrentValue = 5m;

        var violation = await RealDb.ExpectViolationAsync(() => context.SaveChangesAsync(Token));

        Assert.Equal("ck_inventory_lot_balances_zero_quantity_zero_cost", violation.ConstraintName);
    }

    [RealDbFact]
    public async Task Referenced_rows_cannot_be_physically_deleted()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var (user, _, _) = await session.SeedAsync(context);

        var violation = await RealDb.ExpectViolationAsync(() =>
            context.Database.ExecuteSqlAsync($"DELETE FROM roles WHERE id = {user.RoleId}", Token));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
        Assert.Equal("fk_users_role_id", violation.ConstraintName);
    }

    // ----- Transaction rollback / cleanup -----

    [RealDbFact]
    public async Task Rolled_back_test_data_is_not_left_in_the_database()
    {
        var name = $"Rollback {Unique()}";
        await using (var session = await RealDb.Session.StartAsync())
        {
            await using var context = session.NewContext();
            await session.SeedAsync(context);
            context.Add(new Brand(name));
            await context.SaveChangesAsync(Token);
            Assert.Equal(1, await context.Brands.CountAsync(b => b.Name == name, Token));
        }

        await using var check = await RealDb.Session.StartAsync();
        await using var reader = check.NewContext();
        Assert.Equal(0, await reader.Brands.IgnoreQueryFilters().CountAsync(b => b.Name == name, Token));
    }

    private static async Task<StoreProduct> SeedStoreProductAsync(AgriSage.Infrastructure.Persistence.AgriSageDbContext context)
    {
        var storeId = await context.Stores.Select(s => s.Id).FirstAsync(Token);
        var category = new Category($"C{Unique()}", "Category");
        var product = new Product(category.Id, $"SKU-{Unique()}", "Product");
        var storeProduct = new StoreProduct(storeId, product.Id);
        context.AddRange(category, product, storeProduct);
        await context.SaveChangesAsync(Token);

        return storeProduct;
    }
}
