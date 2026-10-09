using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

// Opt-in PostgreSQL tests. Every fixture and expiration is inside RealDb's rolled-back transaction.
public class InventoryOverviewDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static readonly FixedClock Clock = new();

    // Exactly midnight in Vietnam, while UTC is still October 4.
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    }

    private sealed record Fixture(Guid StoreId, Category Category, Unit Unit, string Tag)
    {
        public StoreProduct AddProduct(AgriSageDbContext db, string suffix, long? minimum = null, Guid? storeId = null)
        {
            var product = new Product(Category.Id, $"P-{Tag}-{suffix}", $"Inventory {Tag} {suffix}");
            product.AddPackaging(Unit.Id, 1, true, true, true, "ACTIVE");
            var sp = new StoreProduct(storeId ?? StoreId, product.Id, $"S-{Tag}-{suffix}", minimum);
            db.AddRange(product, sp);
            return sp;
        }
    }

    private static async Task<Fixture> PrepareAsync(AgriSageDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null)
        {
            store = new Store($"T-{tag}", "Inventory test store", "Test address", "Test");
            db.Stores.Add(store);
        }

        var category = new Category($"C-{tag}", "Inventory test category");
        var unit = new Unit($"U-{tag}", "Test base unit");
        db.AddRange(category, unit);
        await db.SaveChangesAsync(Token);
        return new(store.Id, category, unit, tag);
    }

    private static InventoryLot AddLot(AgriSageDbContext db, StoreProduct sp, string number, int? days,
        long quantity = 10, long reserved = 0, InventoryLotStatus status = InventoryLotStatus.Active)
    {
        var expiry = days is { } d ? Today.AddDays(d) : (DateOnly?)null;
        var lot = new InventoryLot(sp.Id, number, expiryDate: expiry);
        if (quantity > 0)
        {
            lot.ReceiveStock(quantity, 12.345678m);
        }

        if (reserved > 0)
        {
            lot.Reserve(reserved, expiry < Today ? expiry.Value : Today);
        }

        lot.ChangeStatus(status);
        db.InventoryLots.Add(lot);
        return lot;
    }

    private static InventoryService Service(AgriSageDbContext db) => new(db, Clock, new RowLockService(db));

    [RealDbFact]
    public async Task Scheduled_inventory_alerts_page_all_items_deduplicate_daily_and_follow_owner_sales_coverage()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await PrepareAsync(db);
        var salesRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.SalesStaff, Token);
        var deliveryRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.DeliveryStaff, Token);
        var ownerRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.StoreOwner, Token);
        User NewUser(Role role, string suffix) => new(role.Id, suffix, "hash", $"{f.Tag}-{suffix}@example.test", null);
        var staff = NewUser(salesRole, "active"); var locked = NewUser(salesRole, "locked");
        var delivery = NewUser(deliveryRole, "delivery"); locked.ChangeStatus(UserStatus.Locked);
        var owner = NewUser(ownerRole, "owner");
        db.AddRange(staff, locked, delivery, owner, new StoreMember(f.StoreId, owner.Id), new StoreMember(f.StoreId, staff.Id),
            new StoreMember(f.StoreId, locked.Id), new StoreMember(f.StoreId, delivery.Id));
        var low = f.AddProduct(db, "LOW", 2);
        AddLot(db, low, "low", null, 1);
        var empty = f.AddProduct(db, "EMPTY"); // out of stock also alerts without a configured minimum
        var expired = f.AddProduct(db, "PAST");
        var expiring = f.AddProduct(db, "SOON");
        var past = AddLot(db, expired, "past", -1);
        var soon = AddLot(db, expiring, "soon", 1);
        await db.SaveChangesAsync(Token);
        var movements = await db.StockMovements.CountAsync(Token);
        var alerts = new OperationalAlertsService(db, new NotificationWriter(db), Service(db), Clock);
        await alerts.InventoryAlertsAsync(1, 2, Token); // one item per page exercises continuation
        await alerts.InventoryAlertsAsync(1, 2, Token);
        var keys = new[] { $"inventory:LOW_STOCK:{low.Id:N}:20261005", $"inventory:OUT_OF_STOCK:{empty.Id:N}:20261005",
            $"inventory:OUT_OF_STOCK:{expired.Id:N}:20261005",
            $"inventory:EXPIRED:{past.Id:N}:20261005", $"inventory:EXPIRING:{soon.Id:N}:20261005" };
        var notifications = await db.Notifications.AsNoTracking().Where(n => keys.Contains(n.DeduplicationKey!)).ToListAsync(Token);
        Assert.Equal(8, notifications.Count);
        Assert.Equal(5, notifications.Count(n => n.UserId == owner.Id));
        Assert.Equal(3, notifications.Count(n => n.UserId == staff.Id));
        Assert.All(notifications.Where(n => n.NotificationType == "EXPIRY_WARNING"), n => Assert.Equal(owner.Id, n.UserId));
        Assert.Equal(movements, await db.StockMovements.CountAsync(Token));
        Assert.Equal(10, past.Balance.QuantityOnHand); Assert.Equal(10, soon.Balance.QuantityOnHand);
        Assert.Equal(InventoryLotStatus.Active, past.Status); // reminders do not expire or alter physical stock
    }

    [RealDbFact]
    public async Task Summary_distinguishes_on_hand_from_sellable_and_keeps_zero_stock_products()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await PrepareAsync(db);
        var stocked = f.AddProduct(db, "A", 150);
        var empty = f.AddProduct(db, "B", 1);
        var noMinimum = f.AddProduct(db, "C");
        var equalMinimum = f.AddProduct(db, "D", 10);
        AddLot(db, stocked, "valid", 5, 100, 20);
        AddLot(db, stocked, "today", 0, 10);
        AddLot(db, stocked, "undated", null, 20, 5);
        AddLot(db, stocked, "past-active", -1, 50);
        AddLot(db, stocked, "blocked", 1, 40, status: InventoryLotStatus.Blocked);
        AddLot(db, stocked, "quarantined", 2, 30, status: InventoryLotStatus.Quarantined);
        AddLot(db, stocked, "expired-status", 3, 20, status: InventoryLotStatus.Expired);
        AddLot(db, stocked, "empty", 0, 0);
        AddLot(db, equalMinimum, "equal", null);
        var deleted = AddLot(db, stocked, "deleted", -2, 999);
        deleted.MarkDeleted(null, Clock.UtcNow);
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();

        var service = Service(db);
        var result = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id }, Token);

        Assert.Equal(4, result.TotalCount);
        var row = Assert.Single(result.Items, r => r.StoreProductId == stocked.Id);
        Assert.Equal(f.Unit.Code, row.BaseUnit);
        Assert.Equal(270, row.OnHandBaseQuantity);
        Assert.Equal(25, row.ReservedBaseQuantity);
        Assert.Equal(245, row.AvailableBaseQuantity);
        Assert.Equal(105, row.SellableAvailableBaseQuantity);
        Assert.Equal(270 * 12.345678m, row.StockValue);
        Assert.Equal(Today, row.NearestExpiryDate);
        Assert.Equal(8, row.LotCount);
        Assert.True(row.IsLowStock);
        var zero = Assert.Single(result.Items, r => r.StoreProductId == empty.Id);
        Assert.Equal(0, zero.OnHandBaseQuantity);
        Assert.Equal(0, zero.StockValue);
        Assert.Equal(0, zero.LotCount);
        Assert.Null(zero.NearestExpiryDate);
        Assert.True(zero.IsLowStock);
        Assert.False(Assert.Single(result.Items, r => r.StoreProductId == noMinimum.Id).IsLowStock);
        Assert.False(Assert.Single(result.Items, r => r.StoreProductId == equalMinimum.Id).IsLowStock);
        Assert.Empty(db.ChangeTracker.Entries());

        var low = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id, LowStockOnly = true }, Token);
        Assert.Equal(2, low.TotalCount);
        var noStock = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id, HasStock = false }, Token);
        Assert.Equal(2, noStock.TotalCount);
        var hasStock = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id, HasStock = true }, Token);
        Assert.Equal(2, hasStock.TotalCount);
        var search = await service.GetStockSummaryAsync(new() { Search = $"  p-{f.Tag}-a  " }, Token);
        Assert.Equal(stocked.Id, Assert.Single(search.Items).StoreProductId);
        var storeSku = await service.GetStockSummaryAsync(new() { Search = $"s-{f.Tag}-a" }, Token);
        Assert.Equal(stocked.Id, Assert.Single(storeSku.Items).StoreProductId);
        var name = await service.GetStockSummaryAsync(new() { Search = $"inventory {f.Tag} a" }, Token);
        Assert.Equal(stocked.Id, Assert.Single(name.Items).StoreProductId);
        var page2 = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id, Page = 2, PageSize = 1 }, Token);
        Assert.Equal(4, page2.TotalCount);
        Assert.Equal(result.Items[1].StoreProductId, Assert.Single(page2.Items).StoreProductId);
    }

    [RealDbFact]
    public async Task Alerts_use_inclusive_Vietnam_windows_all_statuses_and_one_shared_page()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await PrepareAsync(db);
        var sp = f.AddProduct(db, "A", 1000);
        var emptyProduct = f.AddProduct(db, "B", 1);
        var yesterday = AddLot(db, sp, "yesterday", -1);
        var blockedPast = AddLot(db, sp, "blocked-past", -2, status: InventoryLotStatus.Blocked);
        var today = AddLot(db, sp, "today", 0);
        var boundary = AddLot(db, sp, "boundary", 2, status: InventoryLotStatus.Quarantined);
        AddLot(db, sp, "outside", 3);
        AddLot(db, sp, "no-expiry", null);
        AddLot(db, sp, "zero", -1, 0);
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();

        var service = Service(db);
        var all = new List<InventoryAlertItem>();
        long total;
        var page = 1;
        do
        {
            var response = await service.GetAlertsAsync(new() { WithinDays = 2, Page = page++, PageSize = 100 }, Token);
            total = response.TotalCount;
            all.AddRange(response.Items);
        } while (all.Count < total);

        Assert.Equal(total, all.Count);
        Assert.Equal(all.Count, all.Select(r => (r.Type, r.StoreProductId, r.InventoryLotId)).Distinct().Count());
        var ours = all.Where(r => r.StoreProductId == sp.Id).ToList();
        Assert.Equal(5, ours.Count);
        Assert.Equal(-1, Assert.Single(ours, r => r.InventoryLotId == yesterday.Id).DaysToExpiry);
        Assert.Equal("EXPIRED", Assert.Single(ours, r => r.InventoryLotId == blockedPast.Id).Type);
        Assert.Equal("BLOCKED", Assert.Single(ours, r => r.InventoryLotId == blockedPast.Id).LotStatus);
        Assert.Equal(0, Assert.Single(ours, r => r.InventoryLotId == today.Id).DaysToExpiry);
        Assert.Equal("EXPIRING", Assert.Single(ours, r => r.InventoryLotId == boundary.Id).Type);
        Assert.Equal(2, Assert.Single(ours, r => r.InventoryLotId == boundary.Id).DaysToExpiry);
        var low = Assert.Single(ours, r => r.Type == "LOW_STOCK");
        Assert.Null(low.InventoryLotId);
        Assert.Null(low.LotNumber);
        Assert.Null(low.ExpiryDate);
        Assert.Null(low.DaysToExpiry);
        Assert.Null(low.LotStatus);
        Assert.Contains(all, r => r.Type == "LOW_STOCK" && r.StoreProductId == emptyProduct.Id);

        var paged = await service.GetAlertsAsync(new() { WithinDays = 2, PageSize = 2, Page = 2 }, Token);
        Assert.Equal(total, paged.TotalCount);
        Assert.Equal(all.Skip(2).Take(2), paged.Items);
        foreach (var type in new[] { "EXPIRING", "EXPIRED", "LOW_STOCK" })
        {
            var filtered = await service.GetAlertsAsync(new() { Type = type.ToLowerInvariant(), WithinDays = 2 }, Token);
            Assert.Equal(all.LongCount(r => r.Type == type), filtered.TotalCount);
            Assert.All(filtered.Items, r => Assert.Equal(type, r.Type));
        }
    }

    [RealDbFact]
    public async Task Expire_due_changes_only_active_past_lots_preserves_balances_and_is_idempotent()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await PrepareAsync(db);
        var sp = f.AddProduct(db, "A");
        var due = AddLot(db, sp, "due", -1, 100, 20);
        var emptyDue = AddLot(db, sp, "empty-due", -2, 0);
        var retained = new[]
        {
            AddLot(db, sp, "today", 0), AddLot(db, sp, "future", 1), AddLot(db, sp, "undated", null),
            AddLot(db, sp, "blocked", -1, status: InventoryLotStatus.Blocked),
            AddLot(db, sp, "quarantined", -1, status: InventoryLotStatus.Quarantined),
            AddLot(db, sp, "expired", -1, status: InventoryLotStatus.Expired),
            AddLot(db, sp, "depleted", -1, 0, status: InventoryLotStatus.Depleted)
        };
        var expectedStatuses = retained.ToDictionary(l => l.Id, l => l.Status);
        await db.SaveChangesAsync(Token);
        var version = due.Balance.Version;
        var movementsBefore = await db.StockMovements.CountAsync(Token);
        db.ChangeTracker.Clear();

        var service = new InventoryService(db, Clock, new RowLockService(db), new AuditTrail(db, session.CurrentUser, Clock));
        var response = await service.ExpireDueLotsAsync(Token);

        Assert.Equal(response.Lots.Count, response.ExpiredLotCount);
        Assert.Contains(response.Lots, l => l.Id == due.Id && l.ExpiryDate == Today.AddDays(-1));
        Assert.Contains(response.Lots, l => l.Id == emptyDue.Id);
        var log = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "INVENTORY_LOTS_EXPIRED" && a.StoreId == f.StoreId, Token);
        Assert.Null(log.ActorUserId);
        Assert.Contains(due.Id.ToString(), log.NewValues);
        Assert.DoesNotContain(response.Lots, l => expectedStatuses.ContainsKey(l.Id));
        db.ChangeTracker.Clear();
        var persisted = await db.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == due.Id, Token);
        Assert.Equal(InventoryLotStatus.Expired, persisted.Status);
        Assert.Equal(100, persisted.Balance.QuantityOnHand);
        Assert.Equal(20, persisted.Balance.QuantityReserved);
        Assert.Equal(1234.5678m, persisted.Balance.TotalCostValue);
        Assert.Equal(version, persisted.Balance.Version);
        Assert.Equal(movementsBefore, await db.StockMovements.CountAsync(Token));
        foreach (var (id, status) in expectedStatuses)
        {
            Assert.Equal(status, await db.InventoryLots.Where(l => l.Id == id).Select(l => l.Status).SingleAsync(Token));
        }

        await using var secondContext = session.NewContext();
        var again = await Service(secondContext).ExpireDueLotsAsync(Token);
        Assert.Equal(0, again.ExpiredLotCount);
        Assert.Empty(again.Lots);
    }

    [RealDbFact]
    public async Task Other_stores_and_soft_deleted_rows_are_excluded_from_all_three_use_cases()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await PrepareAsync(db);
        var otherStore = new Store($"O-{f.Tag}", "Inactive store", "Test", "Test", status: StoreStatus.Inactive);
        db.Stores.Add(otherStore);
        var other = f.AddProduct(db, "other", 100, otherStore.Id);
        var removedProduct = f.AddProduct(db, "removed", 100);
        var liveProduct = f.AddProduct(db, "live");
        var excludedLots = new[]
        {
            AddLot(db, other, "other", -1), AddLot(db, removedProduct, "removed-product", -1),
            AddLot(db, liveProduct, "removed-lot", -1)
        };
        removedProduct.MarkDeleted(null, Clock.UtcNow);
        excludedLots[2].MarkDeleted(null, Clock.UtcNow);
        await db.SaveChangesAsync(Token);
        db.ChangeTracker.Clear();

        var service = Service(db);
        var summary = await service.GetStockSummaryAsync(new() { CategoryId = f.Category.Id }, Token);
        Assert.Equal(liveProduct.Id, Assert.Single(summary.Items).StoreProductId);
        Assert.Equal(0, summary.Items[0].LotCount);
        var alerts = await service.GetAlertsAsync(new() { PageSize = 100 }, Token);
        Assert.DoesNotContain(alerts.Items, r => r.StoreProductId == other.Id || r.StoreProductId == removedProduct.Id
            || r.StoreProductId == liveProduct.Id);
        var expired = await service.ExpireDueLotsAsync(Token);
        var ids = excludedLots.Select(l => l.Id).ToArray();
        Assert.DoesNotContain(expired.Lots, l => ids.Contains(l.Id));
        Assert.All(await db.InventoryLots.IgnoreQueryFilters().Where(l => ids.Contains(l.Id))
            .Select(l => l.Status).ToListAsync(Token), status => Assert.Equal(InventoryLotStatus.Active, status));
    }
}
