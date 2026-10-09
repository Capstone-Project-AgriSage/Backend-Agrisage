using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Runs the real SaveChanges pipeline with the AGRI-13 interceptors (database design §35.15). A final test
// interceptor suppresses the database write, so the tracked state EF would send is asserted without PostgreSQL.
// A real DbUpdateConcurrencyException needs a database and is verified after AGRI-14.
public class SaveChangesInterceptorTests
{
    private static readonly DateTimeOffset CreatedTime = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterTime = CreatedTime.AddHours(1);

    private readonly FakeClock _clock = new() { UtcNow = CreatedTime };
    private readonly FakeCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ----- Audit timestamps -----

    [Fact]
    public async Task Added_entity_gets_created_and_updated_at_from_the_clock()
    {
        await using var context = CreateContext();
        var brand = new Brand("Brand A");
        context.Add(brand);
        context.Entry(brand).Property(b => b.CreatedAt).CurrentValue = DateTimeOffset.UnixEpoch; // caller value

        await context.SaveChangesAsync(Token);

        Assert.Equal(CreatedTime, brand.CreatedAt);
        Assert.Equal(CreatedTime, brand.UpdatedAt);
    }

    [Fact]
    public async Task Modified_entity_updates_updated_at_and_keeps_created_at()
    {
        await using var context = CreateContext();
        var brand = await AddPersisted(context, new Brand("Brand A"));
        _clock.UtcNow = LaterTime;

        brand.Update("Brand B", code: null, description: null, logoUrl: null);
        context.Entry(brand).Property(b => b.CreatedAt).CurrentValue = DateTimeOffset.UnixEpoch; // tampering
        await context.SaveChangesAsync(Token);

        var entry = context.Entry(brand);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(CreatedTime, brand.CreatedAt);
        Assert.False(entry.Property(b => b.CreatedAt).IsModified);
        Assert.Equal(LaterTime, brand.UpdatedAt);
    }

    [Fact]
    public async Task Unchanged_entity_is_not_touched()
    {
        await using var context = CreateContext();
        var profile = await AddPersisted(context, CreateCreditProfile());
        _clock.UtcNow = LaterTime;

        await context.SaveChangesAsync(Token);

        Assert.Equal(EntityState.Unchanged, context.Entry(profile).State);
        Assert.Equal(CreatedTime, profile.UpdatedAt);
        Assert.Equal(0, profile.Version);
    }

    [Fact]
    public async Task Audit_log_can_be_inserted_but_not_updated_or_deleted()
    {
        await using var context = CreateContext();
        var auditLog = new AuditLog("PRICE_OVERRIDE", "orders", CreatedTime);
        context.Add(auditLog);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.AcceptAllChanges();

        context.Entry(auditLog).State = EntityState.Modified;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));

        context.Entry(auditLog).State = EntityState.Unchanged;
        context.Remove(auditLog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
    }

    // ----- Soft delete -----

    [Fact]
    public async Task Removing_a_root_soft_deletes_it_instead_of_deleting_the_row()
    {
        await using var context = CreateContext();
        var brand = await AddPersisted(context, new Brand("Brand A"));
        _clock.UtcNow = LaterTime;

        context.Remove(brand);
        await context.SaveChangesAsync(Token);

        var entry = context.Entry(brand);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(LaterTime, brand.DeletedAt);
        Assert.Equal(_currentUser.UserId, brand.DeletedBy);
        Assert.Equal(LaterTime, brand.UpdatedAt);
        Assert.Equal(
            [nameof(Brand.DeletedAt), nameof(Brand.DeletedBy), nameof(Brand.UpdatedAt)],
            ModifiedProperties(entry));
    }

    [Fact]
    public async Task Without_an_authenticated_user_deleted_by_is_null()
    {
        _currentUser.UserId = null;
        await using var context = CreateContext();
        var brand = await AddPersisted(context, new Brand("Brand A"));

        context.Remove(brand);
        await context.SaveChangesAsync(Token);

        Assert.NotNull(brand.DeletedAt);
        Assert.Null(brand.DeletedBy);
    }

    [Fact]
    public async Task Changes_made_before_remove_are_kept()
    {
        await using var context = CreateContext();
        var brand = await AddPersisted(context, new Brand("Brand A"));
        _clock.UtcNow = LaterTime;

        brand.Update("Brand B", code: "B", description: null, logoUrl: null);
        context.Remove(brand);
        await context.SaveChangesAsync(Token);

        var entry = context.Entry(brand);
        Assert.Equal("Brand B", brand.Name);
        Assert.Equal(
            [nameof(Brand.Code), nameof(Brand.DeletedAt), nameof(Brand.DeletedBy), nameof(Brand.Name), nameof(Brand.UpdatedAt)],
            ModifiedProperties(entry));
    }

    [Fact]
    public async Task Soft_delete_keeps_the_original_version_and_increments_the_current_one()
    {
        await using var context = CreateContext();
        var profile = await AddPersisted(context, CreateCreditProfile(), version: 7);
        var order = await AddPersisted(context, CreatePendingOrder(), version: 3);

        context.Remove(profile);
        context.Remove(order);
        await context.SaveChangesAsync(Token);

        AssertSoftDeletedWithVersion(context.Entry(profile), originalVersion: 7);
        AssertSoftDeletedWithVersion(context.Entry(order), originalVersion: 3);
    }

    [Fact]
    public async Task Delete_rejected_by_the_domain_leaves_the_entry_untouched()
    {
        await using var context = CreateContext();
        var account = await AddPersisted(context, new DebtAccount(Guid.NewGuid(), Guid.NewGuid()), version: 5);

        context.Remove(account);
        await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync(Token));

        var entry = context.Entry(account);
        Assert.Equal(EntityState.Deleted, entry.State);
        Assert.Null(account.DeletedAt);
        Assert.Equal(5, entry.Property(a => a.Version).OriginalValue);
        Assert.Equal(5, account.Version);
    }

    [Fact]
    public async Task Aggregate_children_cannot_be_removed_directly()
    {
        await using var context = CreateContext();
        var (cart, cartItem) = CreateCartWithItem();
        await AddPersisted(context, cart);
        var lot = await AddPersisted(context, new InventoryLot(Guid.NewGuid(), "LOT-1"));

        context.Remove(cartItem);
        await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync(Token));
        Assert.Null(cartItem.DeletedAt);

        context.Entry(cartItem).State = EntityState.Unchanged;
        context.Remove(lot.Balance);
        await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync(Token));
        Assert.Null(lot.Balance.DeletedAt);
        Assert.Equal(EntityState.Deleted, context.Entry(lot.Balance).State);
    }

    [Fact]
    public async Task Soft_delete_through_the_aggregate_root_keeps_the_values_passed_by_the_application()
    {
        await using var context = CreateContext();
        var (cart, cartItem) = CreateCartWithItem();
        await AddPersisted(context, cart);
        var actorId = Guid.NewGuid();
        _clock.UtcNow = LaterTime;

        cart.RemoveItem(cartItem.Id, actorId, CreatedTime.AddMinutes(30));
        await context.SaveChangesAsync(Token);

        Assert.Equal(EntityState.Modified, context.Entry(cartItem).State);
        Assert.Equal(actorId, cartItem.DeletedBy);
        Assert.Equal(CreatedTime.AddMinutes(30), cartItem.DeletedAt);
        Assert.Equal(LaterTime, cartItem.UpdatedAt);
    }

    [Fact]
    public async Task Removing_an_already_deleted_entity_is_rejected()
    {
        await using var context = CreateContext();
        var brand = new Brand("Brand A");
        brand.MarkDeleted(Guid.NewGuid(), CreatedTime);
        await AddPersisted(context, brand);

        context.Remove(brand);
        await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync(Token));

        Assert.Equal(EntityState.Deleted, context.Entry(brand).State);
        Assert.Equal(CreatedTime, brand.DeletedAt);
    }

    // Without deferred cascade timing, Remove() would null product.BrandId (optional FK) and mark the
    // packaging's required product_id as a conceptual null before the soft delete could happen.
    [Fact]
    public async Task Removing_a_principal_leaves_tracked_dependents_untouched()
    {
        await using var context = CreateContext();
        var brand = await AddPersisted(context, new Brand("Brand A"));
        var product = new Product(Guid.NewGuid(), "SKU-1", "Product 1", brandId: brand.Id);
        var packaging = product.AddPackaging(Guid.NewGuid(), 1, isBaseUnit: true, isPurchaseUnit: true, isSaleUnit: true, "ACTIVE");
        await AddPersisted(context, product);

        context.Remove(brand);
        context.Remove(product);
        await context.SaveChangesAsync(Token);
        context.ChangeTracker.CascadeChanges();

        Assert.Equal(brand.Id, product.BrandId);
        Assert.Equal(product.Id, packaging.ProductId);
        Assert.Equal(EntityState.Modified, context.Entry(brand).State);
        Assert.Equal(EntityState.Modified, context.Entry(product).State);
        Assert.False(context.Entry(product).Property(p => p.BrandId).IsModified);
        Assert.Equal(EntityState.Unchanged, context.Entry(packaging).State);
    }

    // No soft-deletable entity type can reach the database as a physical DELETE: EF Remove() either becomes
    // a soft delete or is rejected by the Domain (always for aggregate children).
    [Fact]
    public async Task No_soft_deletable_entity_type_is_ever_hard_deleted()
    {
        await using var modelContext = CreateContext();
        var softDeletableTypes = modelContext.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(type => type.IsAssignableTo(typeof(SoftDeletableEntity)))
            .ToList();

        Assert.Equal(70, softDeletableTypes.Count);
        var softDeleted = new List<Type>();
        var rejected = new List<Type>();

        foreach (var type in softDeletableTypes)
        {
            await using var context = CreateContext();
            var entity = (SoftDeletableEntity)Activator.CreateInstance(type, nonPublic: true)!;
            context.Attach(entity);
            context.Remove(entity);

            try
            {
                await context.SaveChangesAsync(Token);
            }
            catch (DomainException)
            {
                rejected.Add(type); // Save aborted: nothing is written.
                continue;
            }

            Assert.Equal(EntityState.Modified, context.Entry(entity).State);
            Assert.NotNull(entity.DeletedAt);
            softDeleted.Add(type);
        }

        Assert.Contains(typeof(Brand), softDeleted);
        Assert.Contains(typeof(DebtAccount), rejected);
        Assert.All(
            softDeletableTypes.Where(type => type.IsAssignableTo(typeof(SoftDeletableChildEntity))),
            childType => Assert.Contains(childType, rejected));
    }

    // ----- Concurrency version -----

    [Fact]
    public async Task Added_versioned_entity_starts_at_zero()
    {
        await using var context = CreateContext();
        var profile = CreateCreditProfile();
        context.Add(profile);
        context.Entry(profile).Property(p => p.Version).CurrentValue = 42;

        await context.SaveChangesAsync(Token);

        Assert.Equal(0, profile.Version);
    }

    [Fact]
    public async Task Persisted_update_increments_the_version_once_from_the_original_value()
    {
        await using var context = CreateContext();
        var profile = await AddPersisted(context, CreateCreditProfile(), version: 4);

        profile.ChangeStatus(FarmerCreditProfileStatus.Suspended);
        context.Entry(profile).Property(p => p.Version).CurrentValue = 99; // tampering
        await context.SaveChangesAsync(Token);
        await context.SaveChangesAsync(Token); // not accepted yet: recomputed, not incremented twice

        var version = context.Entry(profile).Property(p => p.Version);
        Assert.True(version.Metadata.IsConcurrencyToken);
        Assert.Equal(4, version.OriginalValue);
        Assert.Equal(5, version.CurrentValue);

        context.ChangeTracker.AcceptAllChanges();
        await context.SaveChangesAsync(Token);

        Assert.Equal(EntityState.Unchanged, context.Entry(profile).State);
        Assert.Equal(5, profile.Version);
    }

    // ----- Helpers -----

    private AgriSageDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AgriSageDbContext>()
            .UseNpgsql("Host=localhost;Database=agrisage_model_only")
            .AddInterceptors(
                new SoftDeleteInterceptor(_currentUser, _clock),
                new AuditableEntityInterceptor(_clock),
                new ConcurrencyVersionInterceptor(),
                new SuppressDatabaseWriteInterceptor())
            .Options);

    // Inserts through the pipeline, then marks the graph as persisted (EF's AcceptAllChanges after a save).
    private static async Task<TEntity> AddPersisted<TEntity>(AgriSageDbContext context, TEntity entity, long? version = null)
        where TEntity : class
    {
        context.Add(entity);
        await context.SaveChangesAsync(Token);

        if (version is not null)
        {
            context.Entry(entity).Property(nameof(IHasConcurrencyVersion.Version)).CurrentValue = version.Value;
        }

        context.ChangeTracker.AcceptAllChanges();
        return entity;
    }

    private void AssertSoftDeletedWithVersion<TEntity>(EntityEntry<TEntity> entry, long originalVersion)
        where TEntity : SoftDeletableEntity, IHasConcurrencyVersion
    {
        var version = entry.Property(nameof(IHasConcurrencyVersion.Version));

        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(originalVersion, version.OriginalValue);
        Assert.Equal(originalVersion + 1, version.CurrentValue);
        Assert.True(version.IsModified);
        Assert.Null(entry.Property(e => e.DeletedAt).OriginalValue);
        Assert.Equal(_clock.UtcNow, entry.Entity.DeletedAt);
    }

    private static string[] ModifiedProperties(EntityEntry entry) =>
        entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).Order().ToArray();

    private static FarmerCreditProfile CreateCreditProfile() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1_000_000m, Guid.NewGuid(), CreatedTime);

    private static Order CreatePendingOrder() =>
        new(Guid.NewGuid(), "ORD-1", OrderSource.Counter, CustomerType.WalkIn, Guid.NewGuid(), "Walk-in customer",
            SettlementType.FullPayment, FulfillmentType.Pickup);

    private static (Cart Cart, CartItem Item) CreateCartWithItem()
    {
        var product = new Product(Guid.NewGuid(), "SKU-1", "Product 1");
        var packaging = product.AddPackaging(Guid.NewGuid(), 1, isBaseUnit: true, isPurchaseUnit: true, isSaleUnit: true, "ACTIVE");
        var storeProduct = new StoreProduct(Guid.NewGuid(), product.Id);
        var cart = new Cart(storeProduct.StoreId, Guid.NewGuid());

        return (cart, cart.SetItemQuantity(storeProduct, packaging, 2));
    }

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid? UserId { get; set; }

        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class SuppressDatabaseWriteInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            InterceptionResult<int>.SuppressWithResult(0);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
    }
}
