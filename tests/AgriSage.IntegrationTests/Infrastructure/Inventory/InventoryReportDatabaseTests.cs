using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

public class InventoryReportDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = new(2026, 10, 5);
    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => BusinessCalendar.StartOfDay(Today);
    }
    private sealed record Fixture(Guid StoreId, Category Category, Unit Unit, User Actor, string Tag)
    {
        public StoreProduct Product(AgriSageDbContext db, string suffix, Guid? storeId = null)
        {
            var p = new Product(Category.Id, $"P-{Tag}-{suffix}", suffix);
            p.AddPackaging(Unit.Id, 1, true, true, true, "ACTIVE");
            var sp = new StoreProduct(storeId ?? StoreId, p.Id, $"S-{Tag}-{suffix}");
            db.AddRange(p, sp);
            return sp;
        }
        public StockMovement Post(AgriSageDbContext db, InventoryLot lot, StockMovementType type, long quantity,
            DateTimeOffset time, decimal cost = 2.345678m, Guid? reversalId = null, Guid? stocktakeId = null)
        {
            var change = quantity > 0 ? lot.ReceiveStock(quantity, cost) : lot.IssueUnreserved(-quantity);
            var m = new StockMovement(StoreId, $"SM-{Guid.NewGuid():N}", type, time, Actor.Id,
                reversalOfMovementId: reversalId, stocktakeId: stocktakeId);
            m.AddItem(lot.Id, change);
            m.Post(Actor.Id, time);
            db.StockMovements.Add(m);
            return m;
        }
    }
    private static async Task<Fixture> SeedAsync(AgriSageDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null) { store = new Store($"T-{tag}", "Report store", "Test", "Test"); db.Add(store); }
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == RoleCode.StoreOwner, Token);
        if (role is null) { role = new Role(RoleCode.StoreOwner, "Owner"); db.Add(role); }
        var actor = new User(role.Id, "Report tester", "hash", $"{tag}@example.test", null);
        var category = new Category($"C-{tag}", "Report category");
        var unit = new Unit($"U-{tag}", "Report unit");
        db.AddRange(actor, category, unit);
        await db.SaveChangesAsync(Token);
        return new(store.Id, category, unit, actor, tag);
    }
    private static InventoryReportService Service(AgriSageDbContext db) => new(db, new Clock());
    private static StockCardRequest Card(Guid product, Guid? lot = null) =>
        new() { StoreProductId = product, InventoryLotId = lot, FromDate = Today, ToDate = Today };

    [RealDbFact]
    public async Task Card_and_movement_report_reconcile_with_balances_for_all_signed_types_and_Vietnam_boundary()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var f = await SeedAsync(db);
        var p = f.Product(db, "stocked");
        var empty = f.Product(db, "empty");
        var lot = new InventoryLot(p.Id, "L1"); var second = new InventoryLot(p.Id, "L2");
        db.AddRange(lot, second);
        var start = BusinessCalendar.StartOfDay(Today);
        f.Post(db, lot, StockMovementType.StockIn, 100, start.AddTicks(-10));
        f.Post(db, second, StockMovementType.StockIn, 50, start.AddDays(-2));
        var stockIn = f.Post(db, lot, StockMovementType.StockIn, 20, start);
        f.Post(db, lot, StockMovementType.Sale, -30, start.AddHours(1));
        f.Post(db, lot, StockMovementType.ReturnIn, 5, start.AddHours(2));
        f.Post(db, lot, StockMovementType.AdjustmentIn, 4, start.AddHours(3));
        f.Post(db, lot, StockMovementType.AdjustmentOut, -3, start.AddHours(4));
        f.Post(db, lot, StockMovementType.Reversal, -2, start.AddHours(5), reversalId: stockIn.Id);
        f.Post(db, second, StockMovementType.Reversal, 1, start.AddHours(6), reversalId: stockIn.Id);
        await db.SaveChangesAsync(Token);
        var expectedTodayQuantity = lot.Balance.QuantityOnHand + second.Balance.QuantityOnHand;
        var expectedTodayValue = lot.Balance.TotalCostValue + second.Balance.TotalCostValue;
        db.ChangeTracker.Clear();
        var service = Service(db);
        var card = await service.GetStockCardAsync(Card(p.Id), Token);
        Assert.Equal(150, card.OpeningBaseQuantity);
        Assert.Equal(7, card.Lines.Count);
        Assert.Equal(expectedTodayQuantity, card.ClosingBaseQuantity);
        long running = card.OpeningBaseQuantity;
        foreach (var line in card.Lines)
        {
            running += line.InBaseQuantity - line.OutBaseQuantity;
            Assert.Equal(running, line.BalanceBaseQuantity);
        }
        var report = await service.GetMovementAsync(new() { FromDate = Today, ToDate = Today, CategoryId = f.Category.Id }, Token);
        var row = Assert.Single(report.Rows, r => r.StoreProductId == p.Id);
        Assert.Equal(card.ClosingBaseQuantity, row.ClosingQuantity);
        Assert.Equal(150, row.OpeningQuantity);
        Assert.Equal(20, row.StockIn.Quantity);
        Assert.Equal(-30, row.Sale.Quantity);
        Assert.Equal(5, row.ReturnIn.Quantity);
        Assert.Equal(4, row.AdjustmentIn.Quantity);
        Assert.Equal(-3, row.AdjustmentOut.Quantity);
        Assert.Equal(-1, row.Reversal.Quantity);
        Assert.Equal(CostRounding.RoundMoney(expectedTodayValue), row.ClosingValue);
        Assert.Equal(CostRounding.RoundMoney(-30 * 2.345678m), row.Sale.Value);
        Assert.Equal(report.Rows.Sum(r => r.ClosingValue), report.Totals.ClosingValue);
        Assert.Equal(0, Assert.Single(report.Rows, r => r.StoreProductId == empty.Id).ClosingQuantity);
        var lotCard = await service.GetStockCardAsync(Card(p.Id, lot.Id), Token);
        Assert.Equal(lot.Balance.QuantityOnHand, lotCard.ClosingBaseQuantity);
        Assert.Empty(db.ChangeTracker.Entries());
        // A posting at exactly the next Vietnam midnight belongs to tomorrow, not today's card.
        var liveSecond = await db.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == second.Id, Token);
        f.Post(db, liveSecond, StockMovementType.StockIn, 8, start.AddDays(1));
        await db.SaveChangesAsync(Token); db.ChangeTracker.Clear();
        Assert.Equal(expectedTodayQuantity, (await service.GetStockCardAsync(Card(p.Id), Token)).ClosingBaseQuantity);
        var tomorrowCard = await service.GetStockCardAsync(Card(p.Id) with { ToDate = Today.AddDays(1) }, Token);
        Assert.Equal(expectedTodayQuantity + 8, tomorrowCard.ClosingBaseQuantity);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [RealDbFact]
    public async Task Valuation_uses_physical_costs_of_all_statuses_and_expiry_date_without_subtracting_reservations()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext(); var f = await SeedAsync(db);
        var p = f.Product(db, "stocked"); var empty = f.Product(db, "empty");
        var lots = new[] { new InventoryLot(p.Id, "past", expiryDate: Today.AddDays(-1)),
            new InventoryLot(p.Id, "today", expiryDate: Today), new InventoryLot(p.Id, "blocked"),
            new InventoryLot(p.Id, "quarantined"), new InventoryLot(p.Id, "expired-status", expiryDate: Today.AddDays(5)) };
        db.AddRange(lots);
        for (var index = 0; index < lots.Length; index++)
            f.Post(db, lots[index], StockMovementType.StockIn, index == 1 ? 20 : 10,
                BusinessCalendar.StartOfDay(Today.AddDays(-2)), cost: index == 0 ? 1 : index == 1 ? 3 : 5);
        var additional = f.Product(db, "additional");
        var additionalLot = new InventoryLot(additional.Id); db.Add(additionalLot);
        f.Post(db, additionalLot, StockMovementType.StockIn, 7, BusinessCalendar.StartOfDay(Today), cost: 4);
        lots[1].Reserve(5, Today); lots[2].ChangeStatus(InventoryLotStatus.Blocked);
        lots[3].ChangeStatus(InventoryLotStatus.Quarantined); lots[4].ChangeStatus(InventoryLotStatus.Expired);
        await db.SaveChangesAsync(Token); db.ChangeTracker.Clear();
        var result = await Service(db).GetValuationAsync(new() { CategoryId = f.Category.Id }, Token);
        var row = Assert.Single(result.Rows, r => r.StoreProductId == p.Id);
        Assert.Equal(60, row.OnHandBaseQuantity);
        Assert.Equal(3.666667m, row.AverageUnitCost);
        Assert.Equal(CostRounding.RoundMoney(lots.Sum(l => l.Balance.TotalCostValue)), row.StockValue);
        Assert.Equal(CostRounding.RoundMoney(lots[0].Balance.TotalCostValue), row.ExpiredValue);
        Assert.Null(Assert.Single(result.Rows, r => r.StoreProductId == empty.Id).AverageUnitCost);
        Assert.Equal(result.Rows.Sum(r => r.StockValue), result.Totals.StockValue);
        Assert.Equal(248, result.Totals.StockValue);
        Assert.Equal(result.Totals.StockValue, Assert.Single(result.ByCategory).StockValue);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [RealDbFact]
    public async Task Card_excludes_drafts_cancellations_deleted_lots_and_checks_lot_ownership_and_source_reference()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext(); var f = await SeedAsync(db);
        var p = f.Product(db, "one"); var other = f.Product(db, "two");
        var lot = new InventoryLot(p.Id); var removed = new InventoryLot(p.Id, "deleted");
        var otherLot = new InventoryLot(other.Id, "other"); db.AddRange(lot, removed, otherLot);
        var take = new Stocktake(f.StoreId, $"ST-{f.Tag}", f.Actor.Id); db.Add(take);
        f.Post(db, lot, StockMovementType.AdjustmentIn, 10, BusinessCalendar.StartOfDay(Today), stocktakeId: take.Id);
        f.Post(db, removed, StockMovementType.StockIn, 99, BusinessCalendar.StartOfDay(Today));
        removed.MarkDeleted(null, new Clock().UtcNow);
        foreach (var cancel in new[] { false, true })
        {
            var m = new StockMovement(f.StoreId, $"SM-{Guid.NewGuid():N}", StockMovementType.StockIn, new Clock().UtcNow, f.Actor.Id);
            m.AddItem(lot.Id, new LotBalanceChange(999, 1, 999, 1009, 999));
            if (cancel) m.Cancel(); db.Add(m);
        }
        await db.SaveChangesAsync(Token); db.ChangeTracker.Clear();
        var card = await Service(db).GetStockCardAsync(Card(p.Id), Token);
        var line = Assert.Single(card.Lines);
        Assert.Equal(10, card.ClosingBaseQuantity);
        Assert.Null(line.LotNumber);
        Assert.Equal(new StockCardReference("STOCKTAKE", take.Id, take.StocktakeNumber), line.Reference);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetStockCardAsync(Card(p.Id, otherLot.Id), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GetStockCardAsync(Card(Guid.NewGuid()), Token));
    }

    [RealDbFact]
    public async Task Reports_exclude_other_stores_and_soft_deleted_products_and_return_empty_for_unknown_category()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext(); var f = await SeedAsync(db);
        var otherStore = new Store($"O-{f.Tag}", "Inactive", "Test", "Test", status: StoreStatus.Inactive); db.Add(otherStore);
        var live = f.Product(db, "live"); var deleted = f.Product(db, "deleted"); var foreign = f.Product(db, "foreign", otherStore.Id);
        var lot = new InventoryLot(live.Id); var deletedLot = new InventoryLot(deleted.Id); var foreignLot = new InventoryLot(foreign.Id);
        db.AddRange(lot, deletedLot, foreignLot);
        f.Post(db, lot, StockMovementType.StockIn, 10, new Clock().UtcNow);
        f.Post(db, deletedLot, StockMovementType.StockIn, 20, new Clock().UtcNow);
        // A foreign lot cannot be pulled into the active store's report even if a movement links to it.
        f.Post(db, foreignLot, StockMovementType.StockIn, 30, new Clock().UtcNow);
        deleted.MarkDeleted(null, new Clock().UtcNow);
        await db.SaveChangesAsync(Token); db.ChangeTracker.Clear();
        var service = Service(db);
        var valuation = await service.GetValuationAsync(new() { CategoryId = f.Category.Id }, Token);
        Assert.Equal(live.Id, Assert.Single(valuation.Rows).StoreProductId);
        var report = await service.GetMovementAsync(new() { FromDate = Today, ToDate = Today, CategoryId = f.Category.Id }, Token);
        Assert.Equal(live.Id, Assert.Single(report.Rows).StoreProductId);
        Assert.Equal(10, report.Rows[0].ClosingQuantity);
        foreach (var id in new[] { deleted.Id, foreign.Id })
            await Assert.ThrowsAsync<NotFoundException>(() => service.GetStockCardAsync(Card(id), Token));
        var noCategory = await service.GetValuationAsync(new() { CategoryId = Guid.NewGuid() }, Token);
        Assert.Empty(noCategory.Rows); Assert.Empty(noCategory.ByCategory); Assert.Equal(0, noCategory.Totals.StockValue);
    }
}
