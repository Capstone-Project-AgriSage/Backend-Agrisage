using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Returns;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using AgriSage.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Returns;

// Domain creates fulfilled fixtures; every API use case runs in a fresh request scope and the session rolls back.
public class SalesReturnDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class DebtStub(decimal budget = 0, bool fail = false) : IDebtReturnPosting
    {
        private decimal remaining = budget;
        public List<(Guid Order, Guid Return, decimal Value, Guid? Source)> Calls { get; } = [];
        public Task<decimal> ApplyReturnAsync(Guid orderId, Guid salesReturnId, decimal returnValue, Guid actorId, Guid? sourceStockMovementId, CancellationToken token)
        {
            Calls.Add((orderId, salesReturnId, returnValue, sourceStockMovementId));
            if (fail) throw new BusinessRuleException("Test debt posting failure.");
            var applied = Math.Min(remaining, returnValue); remaining -= applied; return Task.FromResult(applied);
        }
    }
    private sealed record Env(RealDb.Session Session, Guid Staff, Guid FarmerUser, Guid Farmer, Guid Order,
        Guid OrderItem, Guid Lot, Guid SaleItem, Guid Sale, Guid? Allocation, string Tag)
    {
        public ReturnItemRequest Line(long quantity) => new(OrderItem, quantity, "OTHER", Allocation, Allocation is null ? SaleItem : null);
        private (SalesReturnService Staff, MySalesReturnService My) Services(AgriSageDbContext db, IDebtReturnPosting? debt)
        {
            var source = new ReturnSources(db); var queries = new SalesReturnQueries(db, source); var clock = new Clock();
            var service = new SalesReturnService(db, new RowLockService(db), Session.CurrentUser, clock,
                new AuditTrail(db, Session.CurrentUser, clock), queries, source, debt ?? new StubDebtReturnPosting());
            return (service, new(db, Session.CurrentUser, service, queries));
        }
        public async Task<T> Run<T>(Func<SalesReturnService, Task<T>> call, IDebtReturnPosting? debt = null)
        {
            Session.CurrentUser.UserId = Staff; Session.CurrentUser.Role = "STORE_OWNER";
            await using var db = Session.NewContext(); return await call(Services(db, debt).Staff);
        }
        public async Task<T> My<T>(Func<MySalesReturnService, Task<T>> call, Guid? user = null)
        {
            Session.CurrentUser.UserId = user ?? FarmerUser; Session.CurrentUser.Role = "FARMER";
            await using var db = Session.NewContext(); return await call(Services(db, null).My);
        }
        public Task<SalesReturnResponse> Create(params ReturnItemRequest[] lines) => Run(s => s.CreateAsync(new(Order, lines, Note: Tag), Token));
        public Task<SalesReturnResponse> Get(Guid id) => Run(s => s.GetAsync(id, Token));
        public async Task<SalesReturnResponse> Inspect(Guid id, string condition = "RESELLABLE")
        {
            await Run(s => s.ApproveAsync(id, Token)); var received = await Run(s => s.ReceiveAsync(id, Token));
            foreach (var item in received.Items) await Run(s => s.InspectAsync(id, item.Id, new(condition, "Test inspection"), Token));
            return await Get(id);
        }
    }
    private static async Task<Env> Prepare(RealDb.Session session, bool delivery = false, bool fulfilled = true, decimal price = 1000m, long packages = 10, bool splitPickup = false)
    {
        await using var db = session.NewContext(); var tag = Guid.NewGuid().ToString("N")[..10]; var now = DateTimeOffset.UtcNow;
        async Task<User> Actor(RoleCode code)
        {
            var role = await db.Roles.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.Code == code, Token);
            if (role is null) { role = new Role(code, code.ToString()); db.Roles.Add(role); }
            var user = new User(role.Id, "Return tester", "hash", $"{tag}-{code}@example.test", null); db.Users.Add(user); return user;
        }
        var staff = await Actor(RoleCode.StoreOwner); var user = await Actor(RoleCode.Farmer); var farmer = new FarmerProfile(user.Id); db.FarmerProfiles.Add(farmer);
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null) { store = new Store($"S-{tag}", "Test", "Address", "Province"); db.Stores.Add(store); }
        var category = new Category($"C-{tag}", "Test"); var unit = new Unit($"U-{tag}", "Test"); var product = new Product(category.Id, $"P-{tag}", "Test return product");
        var packUnit = new Unit($"PACK-{tag}", "Pack"); db.Units.Add(packUnit);
        product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE"); var packaging = product.AddPackaging(packUnit.Id, 3, false, true, true, "ACTIVE", packagingName: "Pack 3");
        var sp = new StoreProduct(store.Id, product.Id); var lot = new InventoryLot(sp.Id, "ORIGINAL"); lot.ReceiveStock(100, 10m);
        var address = new DeliveryAddress("Tester", "0900000000", "Address", "Province");
        var order = new Order(store.Id, $"OD-{tag}", OrderSource.Counter, CustomerType.Registered, staff.Id, "Tester",
            SettlementType.FullPayment, delivery ? FulfillmentType.Delivery : FulfillmentType.Pickup, farmer.Id, deliveryAddress: delivery ? address : null);
        var item = order.AddItem(sp, packaging, product.Sku, product.Name, "Pack 3", packages, price); order.Confirm(staff.Id, now);
        Guid? allocationId = null; var sale = new StockMovement(store.Id, $"SM-{tag}", StockMovementType.Sale, now, staff.Id, orderId: order.Id);
        StockMovementItem? saleItem = null;
        if (fulfilled)
        {
            var firstQuantity = splitPickup ? packages * 3 / 2 : packages * 3;
            saleItem = sale.AddItem(lot.Id, lot.IssueUnreserved(firstQuantity));
            if (splitPickup)
            {
                var secondLot = new InventoryLot(sp.Id, "SECOND"); secondLot.ReceiveStock(100, 20m);
                sale.AddItem(secondLot.Id, secondLot.IssueUnreserved(packages * 3 - firstQuantity)); db.InventoryLots.Add(secondLot);
            }
            sale.Post(staff.Id, now);
            if (delivery)
            {
                var member = new StoreMember(store.Id, staff.Id); db.StoreMembers.Add(member);
                var d = new Delivery(order, $"DL-{tag}", address, staff.Id); var di = d.AddItem(item, packages);
                var allocation = d.AllocateLot(di.Id, lot.Id, packages * 3); allocationId = allocation.Id;
                d.Assign(member.Id); d.Dispatch(now); var attempt = d.StartAttempt(member.Id, now, new Dictionary<Guid, long> { [allocation.Id] = packages * 3 });
                d.CompleteAttempt(attempt.Id, now, new Dictionary<Guid, long> { [allocation.Id] = packages * 3 }, "Tester", "https://example.test/proof.jpg");
                d.LinkSaleStockMovement(attempt.Id, sale.Id); db.Deliveries.Add(d);
            }
            order.RecordFulfillment(item.Id, packages * 3, staff.Id, now);
            db.StockMovements.Add(sale);
        }
        db.AddRange(category, unit, product, sp, lot, order); await db.SaveChangesAsync(Token);
        return new(session, staff.Id, user.Id, farmer.Id, order.Id, item.Id, lot.Id, saleItem?.Id ?? Guid.NewGuid(), sale.Id, allocationId, tag);
    }
    [RealDbFact]
    public async Task Split_pickup_cannot_return_more_than_one_source_even_when_order_item_has_capacity()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session, splitPickup: true);
        var available = await env.Run(s => s.ReturnableAsync(env.Order, Token)); var item = Assert.Single(available.Items);
        Assert.Equal(30, item.ReturnableBaseQuantity); Assert.Equal(2, item.Sources.Count);
        Assert.All(item.Sources, s => Assert.Equal(15, s.ReturnableBaseQuantity));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(16)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(10), env.Line(6)));
        var second = item.Sources.Single(s => s.OriginalStockMovementItemId != env.SaleItem);
        var r = await env.Create(env.Line(15), new ReturnItemRequest(env.OrderItem, 15, "OTHER", OriginalStockMovementItemId: second.OriginalStockMovementItemId));
        await env.Inspect(r.Id); var result = await env.Run(s => s.CompleteInspectionAsync(r.Id, Token));
        Assert.Equal(10m, result.Items.Single(i => i.InventoryLotId == env.Lot).OriginalCogsUnitCost);
        Assert.Equal(20m, result.Items.Single(i => i.InventoryLotId != env.Lot).OriginalCogsUnitCost);
        Assert.Single(result.Items.Select(i => i.ReturnStockMovementId).Distinct());
        Assert.Equal(0, (await env.Run(s => s.ReturnableAsync(env.Order, Token))).Items[0].ReturnableBaseQuantity);
    }

    [RealDbFact]
    public async Task In_flight_returns_consume_item_and_source_capacity_remove_cancel_and_reject_release_it()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session);
        var before = await env.Run(s => s.ReturnableAsync(env.Order, Token)); Assert.Equal(30, Assert.Single(before.Items).ReturnableBaseQuantity);
        var r = await env.Create(env.Line(7)); Assert.Equal("REQUESTED", r.Status); Assert.Equal(2333.33m, r.TotalReturnAmount);
        var available = await env.Run(s => s.ReturnableAsync(env.Order, Token)); Assert.Equal(23, available.Items[0].ReturnableBaseQuantity);
        Assert.Equal(7, Assert.Single(available.Items[0].Sources).AlreadyReturnedBaseQuantity);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(24)));
        var added = await env.Run(s => s.AddItemAsync(r.Id, env.Line(3), Token)); Assert.Equal(3333.33m, added.TotalReturnAmount);
        var removed = await env.Run(s => s.RemoveItemAsync(r.Id, added.Items.Single(i => i.ReturnedBaseQuantity == 3).Id, Token)); Assert.Single(removed.Items);
        var rejected = await env.Run(s => s.RejectAsync(r.Id, new("Not eligible"), Token)); Assert.Equal("REJECTED", rejected.Status);
        var all = await env.Create(env.Line(30)); Assert.Equal(10000m, all.TotalReturnAmount);
        await env.Run(s => s.CancelAsync(all.Id, new("Changed mind"), Token));
        Assert.Equal(30, (await env.Run(s => s.ReturnableAsync(env.Order, Token))).Items[0].ReturnableBaseQuantity);
        await using var db = session.NewContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == r.Id && a.Action == "RETURN_REJECTED" && a.Reason == "Not eligible", Token));
        Assert.Equal(2, await db.SalesReturnItems.IgnoreQueryFilters().CountAsync(i => i.SalesReturnId == r.Id, Token));
    }
    [RealDbFact]
    public async Task Restock_uses_original_lot_and_sale_cost_write_off_does_not_add_stock_and_order_is_unchanged()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session);
        var r = await env.Create(env.Line(3), env.Line(2)); await env.Inspect(r.Id);
        await env.Run(s => s.InspectAsync(r.Id, r.Items.Single(i => i.ReturnedBaseQuantity == 2).Id, new("DAMAGED"), Token));
        await using var db = session.NewContext(); var lot = await db.InventoryLots.Include(l => l.Balance).SingleAsync(l => l.Id == env.Lot, Token);
        lot.ReceiveStock(10, 100m); await db.SaveChangesAsync(Token);
        var version = await db.Orders.Where(o => o.Id == env.Order).Select(o => o.Version).SingleAsync(Token);
        var result = await env.Run(s => s.CompleteInspectionAsync(r.Id, Token)); Assert.Equal("PARTIALLY_RESOLVED", result.Status);
        Assert.Equal(1666.67m, result.TotalRefundAmount); Assert.Empty(result.Refunds);
        Assert.NotNull(result.Items.Single(i => i.ReturnedBaseQuantity == 3).ReturnStockMovementId);
        Assert.Null(result.Items.Single(i => i.ReturnedBaseQuantity == 2).ReturnStockMovementId);
        var movement = await db.StockMovements.Include(m => m.Items).AsNoTracking().SingleAsync(m => m.SalesReturnId == r.Id, Token);
        Assert.Equal(StockMovementType.ReturnIn, movement.MovementType); var entry = Assert.Single(movement.Items);
        Assert.Equal(env.Lot, entry.InventoryLotId); Assert.Equal(10m, entry.UnitCostSnapshot); Assert.Equal(3, entry.QuantityDeltaBase);
        Assert.Equal(83, entry.QuantityOnHandAfter); Assert.Equal(1730m, entry.TotalCostValueAfter);
        Assert.Equal(version, await db.Orders.AsNoTracking().Where(o => o.Id == env.Order).Select(o => o.Version).SingleAsync(Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CompleteInspectionAsync(r.Id, Token)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.AddItemAsync(r.Id, env.Line(1), Token)));
    }
    [RealDbFact]
    public async Task Debt_first_settlement_matches_eight_and_fifteen_million_examples_using_cross_flow_step()
    {
        await using var session = await RealDb.Session.StartAsync();
        foreach (var value in new[] { 8_000_000m, 15_000_000m })
        {
            var env = await Prepare(session, price: value, packages: 1); var r = await env.Create(env.Line(3)); await env.Inspect(r.Id, "UNUSABLE");
            var debt = new DebtStub(10_000_000m); var result = await env.Run(s => s.CompleteInspectionAsync(r.Id, Token), debt);
            Assert.Equal(Math.Min(value, 10_000_000m), result.TotalDebtAdjustment);
            Assert.Equal(Math.Max(value - 10_000_000m, 0), result.TotalRefundAmount);
            Assert.Equal(value == 8_000_000m ? "COMPLETED" : "PARTIALLY_RESOLVED", result.Status);
            Assert.Equal(value == 8_000_000m, result.CompletedAt is not null);
            var call = Assert.Single(debt.Calls); Assert.Equal(env.Order, call.Order); Assert.Equal(r.Id, call.Return); Assert.Equal(env.Sale, call.Source);
            await using var db = session.NewContext(); Assert.False(await db.StockMovements.AnyAsync(m => m.SalesReturnId == r.Id, Token));
        }
    }
    [RealDbFact]
    public async Task Failure_in_debt_posting_leaves_no_restock_movement_or_partial_state_and_uninspected_lines_block_completion()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var r = await env.Create(env.Line(3), env.Line(2));
        await env.Run(s => s.ApproveAsync(r.Id, Token)); await env.Run(s => s.ReceiveAsync(r.Id, Token));
        await env.Run(s => s.InspectAsync(r.Id, r.Items[0].Id, new("RESELLABLE"), Token));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CompleteInspectionAsync(r.Id, Token)));
        Assert.Contains(r.Items[1].Id.ToString(), error.Errors!.Keys);
        await env.Run(s => s.InspectAsync(r.Id, r.Items[1].Id, new("RESELLABLE"), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CompleteInspectionAsync(r.Id, Token), new DebtStub(fail: true)));
        Assert.Equal("RECEIVED", (await env.Get(r.Id)).Status);
        await using var db = session.NewContext(); Assert.False(await db.StockMovements.AnyAsync(m => m.SalesReturnId == r.Id, Token));
        Assert.Equal(70, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.Lot).Select(b => b.QuantityOnHand).SingleAsync(Token));
        Assert.Equal("PARTIALLY_RESOLVED", (await env.Run(s => s.CompleteInspectionAsync(r.Id, Token))).Status);
    }
    [RealDbFact]
    public async Task Farmer_can_only_see_request_and_cancel_own_requested_returns()
    {
        await using var session = await RealDb.Session.StartAsync(); var own = await Prepare(session); var other = await Prepare(session);
        var r = await own.My(s => s.CreateAsync(new(own.Order, [own.Line(3)]), Token));
        Assert.Equal(own.FarmerUser, r.RequestedBy); Assert.Equal(own.Farmer, r.FarmerProfileId);
        Assert.Equal(r.Id, (await own.My(s => s.GetAsync(r.Id, Token))).Id);
        Assert.Contains((await own.My(s => s.ListAsync(new(), Token))).Items, i => i.Id == r.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => other.My(s => s.GetAsync(r.Id, Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => other.My(s => s.CancelAsync(r.Id, new("Wrong owner"), Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => other.My(s => s.ReturnableAsync(own.Order, Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => other.My(s => s.CreateAsync(new(own.Order, [own.Line(1)]), Token)));
        var cancelled = await own.My(s => s.CancelAsync(r.Id, new("No longer needed"), Token)); Assert.Equal("CANCELLED", cancelled.Status);
        var next = await own.My(s => s.CreateAsync(new(own.Order, [own.Line(3)]), Token)); await own.Run(s => s.ApproveAsync(next.Id, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => own.My(s => s.CancelAsync(next.Id, new("Too late"), Token)));
    }
    [RealDbFact]
    public async Task Farmer_never_sees_what_the_goods_cost_the_store_but_the_staff_do()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session);
        var staffReturnable = Assert.Single((await env.Run(s => s.ReturnableAsync(env.Order, Token))).Items);
        Assert.All(staffReturnable.Sources, source => Assert.NotNull(source.OriginalCogsUnitCost));
        var farmerReturnable = Assert.Single((await env.My(s => s.ReturnableAsync(env.Order, Token))).Items);
        Assert.All(farmerReturnable.Sources, source => Assert.Null(source.OriginalCogsUnitCost));
        Assert.Equal(staffReturnable.ReturnableBaseQuantity, farmerReturnable.ReturnableBaseQuantity);

        var requested = await env.My(s => s.CreateAsync(new(env.Order, [env.Line(3)]), Token));
        Assert.All(requested.Items, i => { Assert.Null(i.OriginalCogsUnitCost); Assert.Null(i.ReturnInventoryCostValue); });
        await env.Inspect(requested.Id); var completed = await env.Run(s => s.CompleteInspectionAsync(requested.Id, Token));
        Assert.All(completed.Items, i => { Assert.NotNull(i.OriginalCogsUnitCost); Assert.NotNull(i.ReturnInventoryCostValue); });

        var farmerView = await env.My(s => s.GetAsync(requested.Id, Token));
        Assert.Equal(completed.TotalReturnAmount, farmerView.TotalReturnAmount);
        Assert.All(farmerView.Items, i => { Assert.Null(i.OriginalCogsUnitCost); Assert.Null(i.ReturnInventoryCostValue); });

        var next = await env.My(s => s.CreateAsync(new(env.Order, [env.Line(2)]), Token));
        var cancelled = await env.My(s => s.CancelAsync(next.Id, new("Not needed"), Token));
        Assert.All(cancelled.Items, i => { Assert.Null(i.OriginalCogsUnitCost); Assert.Null(i.ReturnInventoryCostValue); });
    }
    [RealDbFact]
    public async Task Unfulfilled_wrong_order_wrong_source_type_and_overlapping_lines_are_rejected_without_partial_request()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var other = await Prepare(session, fulfilled: false);
        Assert.Equal(0, (await other.Run(s => s.ReturnableAsync(other.Order, Token))).Items[0].ReturnableBaseQuantity);
        await Assert.ThrowsAsync<BusinessRuleException>(() => other.Create(other.Line(1)));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Create(other.Line(1)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(1) with { OriginalStockMovementItemId = other.SaleItem }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(1) with { OriginalStockMovementItemId = null, DeliveryItemLotAllocationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(20), env.Line(20)));
        await using var db = session.NewContext(); Assert.False(await db.SalesReturns.AnyAsync(r => r.OrderId == env.Order, Token));
    }
    [RealDbFact]
    public async Task Delivery_return_traces_delivered_allocation_and_original_sale_cost()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session, delivery: true);
        var available = await env.Run(s => s.ReturnableAsync(env.Order, Token)); Assert.Equal("DELIVERY", available.FulfillmentType);
        var source = Assert.Single(Assert.Single(available.Items).Sources); Assert.Equal(env.Allocation, source.DeliveryItemLotAllocationId); Assert.Null(source.OriginalStockMovementItemId);
        Assert.Equal(10m, source.OriginalCogsUnitCost);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Create(env.Line(1) with { DeliveryItemLotAllocationId = null, OriginalStockMovementItemId = env.SaleItem }));
        var r = await env.Create(env.Line(3)); Assert.NotNull(r.Items[0].DeliveryItemId); Assert.Equal(env.Lot, r.Items[0].InventoryLotId);
        await env.Inspect(r.Id); var result = await env.Run(s => s.CompleteInspectionAsync(r.Id, Token)); Assert.NotNull(result.Items[0].ReturnStockMovementId);
    }
    [RealDbFact]
    public async Task Staff_list_filters_and_state_guards_match_return_lifecycle()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var r = await env.Create(env.Line(1));
        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        var page = await env.Run(s => s.ListAsync(new()
        {
            OrderId = env.Order,
            FarmerProfileId = env.Farmer,
            Search = r.ReturnNumber,
            Status = "REQUESTED",
            FromDate = today,
            ToDate = today
        }, Token)); Assert.Equal(r.Id, Assert.Single(page.Items).Id);
        await Assert.ThrowsAsync<DomainException>(() => env.Run(s => s.ReceiveAsync(r.Id, Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Run(s => s.RemoveItemAsync(r.Id, Guid.NewGuid(), Token)));
        await env.Run(s => s.ApproveAsync(r.Id, Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Run(s => s.RejectAsync(r.Id, new("Too late"), Token)));
        await env.Run(s => s.CancelAsync(r.Id, new("Staff cancel approved"), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Run(s => s.ReceiveAsync(r.Id, Token)));
    }
}
