using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// These fixed natural codes must not race the existing real tests that also create Farmer roles.
[CollectionDefinition("Reference seed database", DisableParallelization = true)]
public sealed class ReferenceSeedDatabaseCollection;

[Collection("Reference seed database")]
public class ReferenceSeedDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [RealDbFact]
    public async Task Seed_creates_the_approved_reference_rows_and_no_other_data()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var store = await TestStoreAsync(context);
        var before = await CountsAsync(context);
        // Accounts exist now (the first Admin); the seed must not add or change any.
        var usersBefore = await context.Users.CountAsync(Token);
        var membersBefore = await context.StoreMembers.CountAsync(Token);
        var result = await Seeder(context, store).SeedAsync(Token);
        var after = await CountsAsync(context);
        Assert.Equal(new DatabaseSeeder.Result(5, 9, 5, 1), after);
        Assert.Equal(after.Total - before.Total, result.Total);
        Assert.Equal(RoleSeeder.Entries.Select(entry => entry.Code).Order(),
            (await context.Roles.AsNoTracking().Select(role => role.Code).ToListAsync(Token)).Order());
        var diseases = await context.Diseases.AsNoTracking().ToListAsync(Token);
        Assert.Equal(DiseaseSeeder.Entries.Select(entry => entry.Code).Order(), diseases.Select(row => row.Code).Order());
        Assert.Equal("HEALTHY", Assert.Single(diseases, row => row.IsHealthyClass).Code);
        Assert.All(diseases, row =>
        {
            Assert.Equal("RICE", row.CropType);
            Assert.Null(row.ScientificName);
            Assert.Null(row.Description);
            Assert.Null(row.Symptoms);
            Assert.Null(row.Causes);
            Assert.Null(row.Prevention);
            Assert.NotEqual(default, row.CreatedAt);
            Assert.Equal(row.CreatedAt, row.UpdatedAt);
            Assert.Null(row.DeletedBy);
        });
        var storedStore = await context.Stores.AsNoTracking().SingleAsync(Token);
        Assert.Equal(store.Code, storedStore.Code);
        Assert.Equal(store.Name, storedStore.Name);
        Assert.Equal(store.AddressLine, storedStore.AddressLine);
        Assert.Equal(store.Province, storedStore.Province);
        Assert.Null(storedStore.Ward);
        Assert.Null(storedStore.District);
        Assert.Null(storedStore.PhoneNumber);
        Assert.Null(storedStore.Email);
        Assert.Null(storedStore.TaxCode);
        Assert.Equal(usersBefore, await context.Users.CountAsync(Token));
        Assert.Equal(membersBefore, await context.StoreMembers.CountAsync(Token));
        Assert.Equal(0, await context.Products.CountAsync(Token));
        Assert.Equal(0, await context.Orders.CountAsync(Token));
        Assert.Equal(0, await context.StockMovements.CountAsync(Token));
        Assert.Equal(0, await context.DebtTransactions.CountAsync(Token));
    }

    [RealDbFact]
    public async Task Second_seed_adds_zero_rows_and_preserves_ids_and_timestamps()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var store = await TestStoreAsync(context);
        await Seeder(context, store).SeedAsync(Token);
        var before = await SnapshotAsync(context);
        // A fresh context proves idempotency comes from persisted natural keys, not the tracker.
        await using var second = session.NewContext();
        Assert.Equal(new DatabaseSeeder.Result(0, 0, 0, 0), await Seeder(second, store).SeedAsync(Token));
        Assert.Equal(before, await SnapshotAsync(second));
    }

    [RealDbFact]
    public async Task Existing_reference_values_are_never_overwritten()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var store = await TestStoreAsync(context);
        await Seeder(context, store).SeedAsync(Token);
        (await context.Roles.FirstAsync(Token)).Update("Edited role", "Custom description");
        (await context.Units.FirstAsync(Token)).Update("Edited unit", "custom");
        (await context.Diseases.FirstAsync(Token)).UpdateContent("Edited disease", "Scientific name", "Description",
            "Symptoms", "Causes", "Prevention");
        (await context.Stores.SingleAsync(Token)).UpdateDetails("Edited store", null, null, null,
            "Edited address", null, null, "Edited province");
        await context.SaveChangesAsync(Token);
        await using var second = session.NewContext();
        Assert.Equal(0, (await Seeder(second, store).SeedAsync(Token)).Total);
        Assert.True(await second.Roles.AnyAsync(row => row.Name == "Edited role" && row.Description == "Custom description", Token));
        Assert.True(await second.Units.AnyAsync(row => row.Name == "Edited unit" && row.Symbol == "custom", Token));
        Assert.True(await second.Diseases.AnyAsync(row => row.Name == "Edited disease" && row.Description == "Description", Token));
        Assert.True(await second.Stores.AnyAsync(row => row.Name == "Edited store" && row.AddressLine == "Edited address", Token));
    }

    [RealDbFact]
    public Task Soft_deleted_role_is_rejected() => RejectDeletedAsync("roles");

    [RealDbFact]
    public Task Soft_deleted_unit_is_rejected() => RejectDeletedAsync("units");

    [RealDbFact]
    public Task Soft_deleted_disease_is_rejected() => RejectDeletedAsync("diseases");

    [RealDbFact]
    public Task Soft_deleted_store_is_rejected() => RejectDeletedAsync("stores");

    [RealDbFact]
    public async Task Another_active_store_prevents_seed_without_partial_rows()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        context.Stores.Add(new Store($"OTHER-{Guid.NewGuid():N}"[..30], "Other store", "Test address", "Test province"));
        await context.SaveChangesAsync(Token);
        var before = await CountsAsync(context);
        var exception = await Assert.ThrowsAsync<ReferenceSeedException>(() => Seeder(context, TestStore()).SeedAsync(Token));
        Assert.Contains("Another ACTIVE store", exception.Message);
        Assert.Equal(before, await CountsAsync(context));
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [RealDbFact]
    public async Task Failure_after_save_rolls_back_all_groups_and_leaves_the_outer_transaction_usable()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext(new FailAfterSave());
        var store = await TestStoreAsync(context);
        var before = await CountsAsync(context);
        await Assert.ThrowsAsync<InjectedSeedFailure>(() => Seeder(context, store).SeedAsync(Token));
        await using var reader = session.NewContext();
        Assert.Equal(before, await CountsAsync(reader));
        Assert.False(context.ChangeTracker.HasChanges());
        // The same transaction still supports a successful invocation after rollback to the savepoint.
        var result = await Seeder(reader, store).SeedAsync(Token);
        Assert.Equal(20 - before.Total, result.Total);
    }

    [RealDbFact]
    public async Task All_seed_rows_disappear_after_the_test_transaction_rolls_back()
    {
        SeedStoreOptions store;
        DatabaseSeeder.Result before;
        (Guid Id, DateTimeOffset Created, DateTimeOffset Updated)[] snapshot;
        bool storeExisted;
        await using (var session = await RealDb.Session.StartAsync())
        {
            await using var context = session.NewContext();
            store = await TestStoreAsync(context);
            before = await CountsAsync(context);
            snapshot = await SnapshotAsync(context);
            storeExisted = await context.Stores.IgnoreQueryFilters().AnyAsync(row => row.Code == store.Code, Token);
            await Seeder(context, store).SeedAsync(Token);
            Assert.True(await context.Stores.AnyAsync(row => row.Code == store.Code, Token));
        }

        await using var verification = await RealDb.Session.StartAsync();
        await using var reader = verification.NewContext();
        Assert.Equal(before, await CountsAsync(reader));
        Assert.Equal(snapshot, await SnapshotAsync(reader));
        Assert.Equal(storeExisted, await reader.Stores.IgnoreQueryFilters().AnyAsync(row => row.Code == store.Code, Token));
    }

    private static async Task RejectDeletedAsync(string table)
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        var store = await TestStoreAsync(context);
        await Seeder(context, store).SeedAsync(Token);
        SoftDeletableEntity row = table switch
        {
            "roles" => await context.Roles.FirstAsync(Token),
            "units" => await context.Units.FirstAsync(Token),
            "diseases" => await context.Diseases.FirstAsync(Token),
            _ => await context.Stores.SingleAsync(Token)
        };
        context.Remove(row);
        await context.SaveChangesAsync(Token);
        var deletedAt = await DeletedAtAsync(context, table, row.Id);
        var before = await CountsAsync(context);
        await using var second = session.NewContext();
        Assert.Contains(table, (await Assert.ThrowsAsync<ReferenceSeedException>(
            () => Seeder(second, store).SeedAsync(Token))).Message);
        Assert.Equal(before, await CountsAsync(second));
        Assert.Equal(deletedAt, await DeletedAtAsync(second, table, row.Id));
    }

    private static Task<DateTimeOffset?> DeletedAtAsync(AgriSageDbContext context, string table, Guid id) =>
        table switch
        {
            "roles" => context.Roles.IgnoreQueryFilters().Where(entity => entity.Id == id).Select(entity => entity.DeletedAt).SingleAsync(Token),
            "units" => context.Units.IgnoreQueryFilters().Where(entity => entity.Id == id).Select(entity => entity.DeletedAt).SingleAsync(Token),
            "diseases" => context.Diseases.IgnoreQueryFilters().Where(entity => entity.Id == id).Select(entity => entity.DeletedAt).SingleAsync(Token),
            _ => context.Stores.IgnoreQueryFilters().Where(entity => entity.Id == id).Select(entity => entity.DeletedAt).SingleAsync(Token)
        };

    private static DatabaseSeeder Seeder(AgriSageDbContext context, SeedStoreOptions store) => new(context, Options.Create(store));

    private static SeedStoreOptions TestStore() => new()
    {
        Code = $"TEST-{Guid.NewGuid():N}"[..30], Name = "Rollback-only test store",
        AddressLine = "Test address", Province = "Test province"
    };

    // The same suite also works after the approved reference seed has been applied. Existing rows
    // are reused inside the rollback-only session; never create a second operational Store.
    private static async Task<SeedStoreOptions> TestStoreAsync(AgriSageDbContext context)
    {
        var store = await context.Stores.AsNoTracking().SingleOrDefaultAsync(Token);
        return store is null ? TestStore() : new SeedStoreOptions
        {
            Code = store.Code, Name = store.Name, AddressLine = store.AddressLine, Province = store.Province,
            Ward = store.Ward, District = store.District, PhoneNumber = store.PhoneNumber,
            Email = store.Email, TaxCode = store.TaxCode
        };
    }

    private static async Task<DatabaseSeeder.Result> CountsAsync(AgriSageDbContext context) => new(
        await context.Roles.IgnoreQueryFilters().CountAsync(Token),
        await context.Units.IgnoreQueryFilters().CountAsync(Token),
        await context.Diseases.IgnoreQueryFilters().CountAsync(Token),
        await context.Stores.IgnoreQueryFilters().CountAsync(Token));

    private static async Task<(Guid Id, DateTimeOffset Created, DateTimeOffset Updated)[]> SnapshotAsync(AgriSageDbContext context)
    {
        var roles = await context.Roles.AsNoTracking().ToListAsync(Token);
        var units = await context.Units.AsNoTracking().ToListAsync(Token);
        var diseases = await context.Diseases.AsNoTracking().ToListAsync(Token);
        var stores = await context.Stores.AsNoTracking().ToListAsync(Token);
        return roles.Cast<AuditableEntity>().Concat(units).Concat(diseases).Concat(stores)
            .OrderBy(row => row.Id).Select(row => (row.Id, row.CreatedAt, row.UpdatedAt)).ToArray();
    }

    private sealed class InjectedSeedFailure : Exception;

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InjectedSeedFailure();
    }
}
