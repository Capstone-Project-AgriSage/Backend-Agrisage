using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Reports;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AgriSage.IntegrationTests.Infrastructure.Inventory;

public class ReplenishmentDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => new(2026, 11, 2, 5, 0, 0, TimeSpan.Zero); }
    [RealDbFact]
    public async Task Planning_queries_rules_risk_and_paging_work_on_the_reviewed_schema_without_touching_public_data()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var schema = "inventory_planning_" + Guid.NewGuid().ToString("N");
        var setup = "CREATE SCHEMA " + schema + "; SET LOCAL search_path TO " + schema + ";";
        await db.Database.ExecuteSqlRawAsync(setup, Token);
        var script = db.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
        await db.Database.ExecuteSqlRawAsync(script, Token);
        var (user, store, _) = await session.SeedAsync(db);
        session.CurrentUser.Role = "STORE_OWNER";
        var today = new DateOnly(2026, 11, 2);
        var category = new Category("PLAN", "Planning category");
        var unit = new Unit("BOTTLE", "Chai");
        var box = new Unit("BOX", "Thùng");
        var p = new Product(category.Id, "PLAN-A", "Planning A");
        p.AddPackaging(unit.Id, 1, true, false, true, "ACTIVE");
        p.AddPackaging(box.Id, 12, false, true, false, "ACTIVE");
        var product = new StoreProduct(store.Id, p.Id, minStockLevelBase: 30);
        product.ConfigureReplenishment(true, 80, 4, 10, 10);
        var lot = new InventoryLot(product.Id, "PLAN-LOT", expiryDate: today.AddDays(5));
        db.AddRange(category, unit, box, p, product, lot);
        void Movement(string number, StockMovementType type, DateTimeOffset at, AgriSage.Domain.Features.Inventory.LotBalanceChange change) {
            var movement = new StockMovement(store.Id, number, type, at, user.Id);
            movement.AddItem(lot.Id, change); movement.Post(user.Id, at); db.Add(movement);
        }
        Movement("PLAN-IN", StockMovementType.StockIn, BusinessCalendar.StartOfDay(today.AddDays(-35)), lot.ReceiveStock(170, 10));
        foreach (var day in new[] { -30, -20, -10 }) Movement("PLAN-SALE" + day, StockMovementType.Sale,
            BusinessCalendar.StartOfDay(today.AddDays(day)), lot.IssueUnreserved(50));
        await db.SaveChangesAsync(Token);
        var clock = new Clock();
        var planningData = new InventoryPlanningData(db, clock);
        var products = new StoreProductService(db, new NpgsqlErrorClassifier());
        var beforeSettings = await products.GetAsync(product.Id, Token);
        Assert.Equal(80, beforeSettings.Replenishment!.TargetStockLevelBase);
        var afterLegacyUpdate = await products.UpdateAsync(product.Id, new UpdateStoreProductRequest(null, 30), Token);
        Assert.Equal(beforeSettings.Replenishment, afterLegacyUpdate.Replenishment);
        var service = new ReplenishmentService(planningData, db);
        var result = await service.GetAsync("suggestions", new() { Search = "PLAN-A", PageSize = 1 }, Token);
        var row = Assert.Single(result.Items);
        Assert.Equal(20, row.SellableBaseQuantity);
        Assert.Equal(60, row.SuggestedBaseQuantity);
        Assert.Equal(5, row.SuggestedPurchaseQuantity);
        Assert.Equal("SUFFICIENT", row.DataStatus);
        Assert.Equal(30, row.Forecast.Count);
        Assert.Contains("LOW_STOCK", row.ReasonCodes);
        var overview = new InventoryService(db, clock, new RowLockService(db));
        Assert.Empty((await overview.GetStockSummaryAsync(new() { Search = "PLAN-A", HasSellableStock = false }, Token)).Items);
        lot.ChangeStatus(InventoryLotStatus.Blocked);
        await db.SaveChangesAsync(Token);
        Assert.Single((await overview.GetAlertsAsync(new() { Search = "PLAN-A", Type = "OUT_OF_STOCK" }, Token)).Items);
        Assert.Single((await overview.GetStockSummaryAsync(new() { Search = "PLAN-A", HasStock = true, HasSellableStock = false }, Token)).Items);
        lot.ChangeStatus(InventoryLotStatus.Active);
        var empty = new Product(category.Id, "PLAN-B", "Planning B");
        empty.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE");
        var emptyStoreProduct = new StoreProduct(store.Id, empty.Id);
        db.AddRange(empty, emptyStoreProduct);
        await db.SaveChangesAsync(Token);
        var unavailable = Assert.Single((await service.GetAsync("suggestions", new() { Search = "PLAN-B" }, Token)).Items);
        Assert.Null(unavailable.AverageDailySales);
        Assert.Null(unavailable.SuggestedBaseQuantity);
        Assert.Null(unavailable.ExpectedDemandBaseQuantity);
        Assert.Contains("NEEDS_CONFIGURATION", unavailable.ReasonCodes);
        Assert.Single((await overview.GetAlertsAsync(new() { Search = "PLAN-B", Type = "OUT_OF_STOCK" }, Token)).Items);
        var firstPage = await service.GetAsync("suggestions", new() { Search = "PLAN-", PageSize = 1 }, Token);
        var secondPage = await service.GetAsync("suggestions", new() { Search = "PLAN-", PageSize = 1, Page = 2 }, Token);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Summary.TotalProducts);
        Assert.Equal(1, firstPage.Summary.OutOfStock);
        Assert.NotEqual(firstPage.Items[0].StoreProductId, secondPage.Items[0].StoreProductId);
        var audit = new AuditTrail(db, session.CurrentUser, clock);
        var rules = new SeasonalRuleService(db, clock, session.CurrentUser, audit);
        var saved = await rules.SaveAsync(null, new("Prepare", "RICE", "Test", today.AddDays(2), today.AddDays(20),
            14, 2m, [new(product.Id, 200)]), Token);
        var seasonal = Assert.Single((await service.GetAsync("seasonal", new() { Search = "PLAN-A" }, Token)).Items);
        Assert.Equal(200, seasonal.TargetBaseQuantity);
        Assert.Equal(180, seasonal.SuggestedBaseQuantity);
        Assert.Contains("SEASONAL", seasonal.ReasonCodes);
        await rules.SaveAsync(saved.Id, new("Prepare updated", "RICE", "Test", saved.StartsOn, saved.EndsOn,
            14, 2, [new(product.Id, 220)], Version: saved.Version), Token);
        await Assert.ThrowsAsync<ConflictException>(() => rules.SaveAsync(saved.Id,
            new("Stale", "RICE", null, saved.StartsOn, saved.EndsOn, 14, 1, [new(product.Id, 500)], Version: saved.Version), Token));
        var list = await rules.ListAsync(new() { Search = "Prepare", PageSize = 1 }, Token);
        Assert.Equal(1, list.Summary.Active);
        await rules.DeactivateAsync(saved.Id, Token);
        Assert.Empty((await service.GetAsync("seasonal", new() { Search = "PLAN-A" }, Token)).Items);
        var reports = new InventoryPlanningReportService(planningData);
        var risk = await reports.ExpiryRiskAsync(new() { Search = "PLAN-A", WithinDays = 30 }, Token);
        Assert.Single(risk.Items);
        Assert.NotNull(risk.Items[0].AtRiskBaseQuantity);
        var velocity = await reports.VelocityAsync(new() { Search = "PLAN-A" }, Token);
        Assert.Equal(150, Assert.Single(velocity.Items).SoldBaseQuantity);
        Assert.Equal(20, (await db.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == lot.Id, Token)).Balance.QuantityOnHand);
        Assert.Equal(4, await db.StockMovements.CountAsync(Token));
        Assert.Contains(await db.AuditLogs.ToListAsync(Token), a => a.Action == "SEASONAL_RULE_CREATED");
        var inventoryReports = new InventoryReportService(db, clock);
        var movement = await inventoryReports.GetMovementAsync(new() { FromDate = today.AddDays(-40), ToDate = today, Search = "PLAN-A" }, Token);
        Assert.Single(movement.Rows);
        Assert.Equal(20, movement.Rows[0].ClosingQuantity);
    }
}
