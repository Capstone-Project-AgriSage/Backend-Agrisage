using System.Runtime.CompilerServices;
using AgriSage.Application;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// Explicit dedicated test DB only. Never falls back to the shared Supabase/user-secrets connection.
public sealed class CreditDbFactAttribute : FactAttribute
{
    public CreditDbFactAttribute([CallerFilePath] string? file = null, [CallerLineNumber] int line = -1) : base(file, line)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")))
            Skip = "Set AGRISAGE_CREDIT_TEST_CONNECTION_STRING to a dedicated PostgreSQL test database (CREATE SCHEMA permission required).";
    }
}

internal sealed class CreditTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnection;
    private readonly string _schema = "credit_test_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? _services;
    private CreditTestDatabase(string connection) => _adminConnection = connection;
    public sealed class UserContext : ICurrentUserService
    { public Guid? UserId { get; set; } public string? Role { get; set; } = "STORE_OWNER"; public bool IsAuthenticated => UserId != null; }
    public sealed class TestClock : IDateTimeProvider
    { public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 4, 17, 30, 0, TimeSpan.Zero); }
    public UserContext User { get; } = new();
    public TestClock Clock { get; } = new();
    public Guid StoreId { get; private set; }
    public Guid FarmerId { get; private set; }
    public Guid FarmerUserId { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid TierId { get; private set; }
    public Guid StoreProductId { get; private set; }
    public Guid PackagingId { get; private set; }
    public Guid LotId { get; private set; }
    public IServiceScope Scope() => _services!.CreateScope();

    public static async Task<CreditTestDatabase> StartAsync(CancellationToken token)
    {
        var d = new CreditTestDatabase(Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Dedicated credit test database is required."));
        try { await d.InitializeAsync(token); return d; }
        catch { await d.DisposeAsync(); throw; }
    }
    private async Task InitializeAsync(CancellationToken token)
    {
        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync(token);
            // Schema is locally generated lowercase/hex, never user supplied.
            await using var cmd = new NpgsqlCommand($"CREATE SCHEMA \"{_schema}\"", admin);
            await cmd.ExecuteNonQueryAsync(token);
        }
        var connection = new NpgsqlConnectionStringBuilder(_adminConnection) { SearchPath = _schema, Pooling = false }.ConnectionString;
        var s = new ServiceCollection();
        s.AddApplication(); s.AddSingleton<ICurrentUserService>(User); s.AddSingleton<IDateTimeProvider>(Clock);
        s.AddSingleton<IDatabaseErrorClassifier, NpgsqlErrorClassifier>();
        s.AddDbContext<AgriSageDbContext>(o => o.UseNpgsql(connection).AddInterceptors(new SoftDeleteInterceptor(User, Clock),
            new AuditableEntityInterceptor(Clock), new ConcurrencyVersionInterceptor()));
        s.AddScoped<IAgriSageDbContext>(p => p.GetRequiredService<AgriSageDbContext>());
        s.AddScoped<IRowLockService, RowLockService>();
        _services = s.BuildServiceProvider();
        using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        await db.Database.MigrateAsync(token);
        var ownerRole = new Role(RoleCode.StoreOwner, "Owner"); var farmerRole = new Role(RoleCode.Farmer, "Farmer");
        var owner = new User(ownerRole.Id, "Owner", "hash", "owner@example.test", null);
        var farmerUser = new User(farmerRole.Id, "Farmer Test", "hash", "farmer@example.test", "0912345678");
        var farmer = new FarmerProfile(farmerUser.Id); var store = new Store("TEST", "Test", "Test", "Test");
        User.UserId = owner.Id; StoreId = store.Id; FarmerId = farmer.Id; FarmerUserId = farmerUser.Id;
        var tier = new CreditTier(store.Id, "STANDARD", "Standard", 20_000_000, 30);
        var profile = new FarmerCreditProfile(store.Id, farmer.Id, 20_000_000, owner.Id, Clock.UtcNow, tier.Id);
        var account = new DebtAccount(store.Id, farmer.Id);
        ProfileId = profile.Id; AccountId = account.Id; TierId = tier.Id;
        var category = new Category("CAT", "Category"); var unit = new Unit("BOTTLE", "Bottle");
        var product = new Product(category.Id, "SKU", "Product");
        var packaging = product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE", "Bottle");
        var storeProduct = new StoreProduct(store.Id, product.Id);
        var lot = new InventoryLot(storeProduct.Id, "LOT", null, new DateOnly(2027, 12, 31));
        lot.ReceiveStock(1000, 100_000);
        StoreProductId = storeProduct.Id; PackagingId = packaging.Id; LotId = lot.Id;
        db.AddRange(ownerRole, farmerRole, owner, farmerUser, farmer, store, tier, profile, account, category, unit, product, storeProduct, lot);
        await db.SaveChangesAsync(token);
        var prices = scope.ServiceProvider.GetRequiredService<IPriceListService>();
        var list = await prices.CreateAsync(new("PRICE", "Price", Clock.UtcNow.AddDays(-1), IsWalkInDefault: true), token);
        await prices.UpsertItemsAsync(list.Id, new([new(StoreProductId, PackagingId, 1_000_000)]), token);
        await prices.ActivateAsync(list.Id, token);
    }
    public async Task<OrderResponse> OrderAsync(long quantity, CancellationToken token)
    {
        using var s = Scope();
        return await s.ServiceProvider.GetRequiredService<IOrderService>().CreateAsync(
            new("REGISTERED", "CREDIT", "PICKUP", [new(StoreProductId, PackagingId, quantity)], FarmerId), token);
    }
    public async Task<OrderResponse> FulfillAsync(long quantity, CancellationToken token)
    {
        var o = await OrderAsync(quantity, token);
        using (var s = Scope()) await s.ServiceProvider.GetRequiredService<IOrderConfirmationService>().ConfirmAsync(o.Id, token);
        using var pickup = Scope();
        return await pickup.ServiceProvider.GetRequiredService<IOrderPickupService>().PickupAsync(o.Id,
            new([new(o.Items.Single().Id, [new(LotId, quantity)])]), token);
    }
    public async ValueTask DisposeAsync()
    {
        if (_services != null) await _services.DisposeAsync();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        // Only the exact generated schema is dropped; production/public objects are outside this target.
        await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", admin);
        await cmd.ExecuteNonQueryAsync();
    }
}
