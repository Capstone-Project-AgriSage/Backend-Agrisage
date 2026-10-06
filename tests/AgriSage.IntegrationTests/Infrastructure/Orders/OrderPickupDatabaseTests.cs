using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Orders;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): pickup, the FulfillmentPostingService step and cancel-remaining (F1.5),
// always rolled back. Active walk-in lists already in agrisage-dev are switched off inside the transaction first.
[Collection(RealDb.WalkInPriceListCollection)]
public class OrderPickupDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static DateOnly Today => BusinessCalendar.Today(Now);

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context, RealDb.MutableUser user)
        {
            Context = context;
            var errors = new NpgsqlErrorClassifier();
            var clock = new DateTimeProvider();
            var audit = new AuditTrail(context, user, clock);
            var locks = new RowLockService(context);
            Categories = new CategoryService(context, errors);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            PriceLists = new PriceListService(context, clock, errors, audit);
            var queries = new OrderQueries(context);
            Orders = new OrderService(
                context, new OrderBuilder(context, new PriceResolver(context), user, clock, audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(context, clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy()))), queries, user, clock, errors, audit);
            Confirmation = new OrderConfirmationService(
                context, locks, new OrderConfirmer(context, locks, new StubOrderSettlementGuard(), user, clock, audit), queries, clock, errors, audit);
            Pickup = new OrderPickupService(
                context, locks, new FulfillmentPostingService(context, locks, new StubFulfillmentFinancialPosting(new OrderPrepaymentLedger(context)), new OrderPaymentCancellation(context, locks, new FakePaymentGateway(), clock, audit)),
                new StubOrderSettlementGuard(), new OrderPaymentCancellation(context, locks, new FakePaymentGateway(), clock, audit), queries, user, clock, audit);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderService Orders { get; }

        public OrderConfirmationService Confirmation { get; }

        public OrderPickupService Pickup { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid Box { get; set; }

        public Guid WalkInListId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Sku(Guid StoreProductId, Guid BottleId, Guid BoxId, string Code);

    private static async Task<Guid> RoleIdAsync(AgriSageDbContext context, RoleCode code)
    {
        var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
        if (role is null)
        {
            role = new Role(code, code.ToString());
            context.Roles.Add(role);
            await context.SaveChangesAsync(Token);
        }

        return role.Id;
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var user = new RealDb.MutableUser { Role = "SALES_STAFF" };
        var env = new Env(session.NewContext(), user);
        var context = env.Context;

        async Task<Guid> UnitAsync(string code, string name)
        {
            var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == code, Token);
            if (unit is null)
            {
                unit = new Unit(code, name);
                context.Units.Add(unit);
                await context.SaveChangesAsync(Token);
            }

            return unit.Id;
        }

        env.Bottle = await UnitAsync("BOTTLE", "Bottle");
        env.Box = await UnitAsync("BOX", "Box");
        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Pickup Tester", "hash", $"{Tag()}@example.test", null);
        context.Users.Add(env.Actor);
        user.UserId = env.Actor.Id;

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var store = new Store($"T{Tag()}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            storeId = store.Id;
        }

        env.StoreId = storeId;

        foreach (var list in await context.PriceLists.Where(p => p.StoreId == storeId && p.Status == PriceListStatus.Active)
                     .ToListAsync(Token))
        {
            list.Deactivate();
        }

        await context.SaveChangesAsync(Token);

        var walkIn = await env.PriceLists.CreateAsync(new PriceListRequest($"PL-{Tag()}", "Bảng giá thử", Now.AddDays(-1), null, true), Token);
        await env.PriceLists.ActivateAsync(walkIn.Id, Token);
        env.WalkInListId = walkIn.Id;

        return env;
    }

    // Bottle (1 base unit) priced 10 000 and Box of 6 priced 55 000, both sold.
    private static async Task<Sku> NewSkuAsync(Env env)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest(
                $"SKU-{tag}", $"Product {tag}", category.Id,
                [
                    new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle"),
                    new PackagingRequest(env.Box, 6, false, true, true, "Box of 6")
                ]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        var bottleId = product.Packagings.Single(p => p.UnitId == env.Bottle).Id;
        var boxId = product.Packagings.Single(p => p.UnitId == env.Box).Id;
        await env.PriceLists.UpsertItemsAsync(
            env.WalkInListId,
            new UpsertPriceListItemsRequest(
            [
                new PriceListItemInput(storeProduct.Id, bottleId, 10_000m),
                new PriceListItemInput(storeProduct.Id, boxId, 55_000m)
            ]),
            Token);

        return new Sku(storeProduct.Id, bottleId, boxId, product.Sku);
    }

    private static async Task<InventoryLot> NewLotAsync(Env env, Sku sku, string number, long onHand, DateOnly? expiry)
    {
        var lot = new InventoryLot(sku.StoreProductId, number, null, expiry);
        lot.ReceiveStock(onHand, 5_000m);
        env.Context.InventoryLots.Add(lot);
        await env.Context.SaveChangesAsync(Token);

        return lot;
    }

    // A confirmed walk-in PICKUP order of `quantity` bottles (reserved by FEFO).
    private static async Task<OrderResponse> ConfirmedOrderAsync(Env env, Sku sku, long quantity, bool box = false)
    {
        var order = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "WALK_IN", "FULL_PAYMENT", "PICKUP",
                [new OrderItemRequest(sku.StoreProductId, box ? sku.BoxId : sku.BottleId, quantity)]),
            Token);

        return await env.Confirmation.ConfirmAsync(order.Id, Token);
    }

    private static PickupRequest Take(Guid orderItemId, params (Guid LotId, long Quantity)[] lots) =>
        new([new PickupItemRequest(orderItemId, lots.Select(l => new PickupLotRequest(l.LotId, l.Quantity)).ToList())]);

    private static async Task<(long OnHand, long Reserved)> BalanceAsync(Env env, Guid lotId)
    {
        var balance = await env.Context.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == lotId, Token);

        return (balance.QuantityOnHand, balance.QuantityReserved);
    }

    // ----- Pickup -----

    [RealDbFact]
    public async Task A_full_pickup_issues_the_reserved_lots_posts_a_sale_and_completes_the_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));
        var order = await ConfirmedOrderAsync(env, sku, 125);
        var itemId = order.Items.Single().Id;

        var done = await env.Pickup.PickupAsync(order.Id, Take(itemId, (early.Id, 100), (late.Id, 25)), Token);

        Assert.Equal(("COMPLETED", env.Actor.Id), (done.Status, done.PickupCompletedBy));
        Assert.NotNull(done.PickupCompletedAt);
        Assert.NotNull(done.CompletedAt);
        var line = done.Items.Single();
        Assert.Equal((125L, 0L, 0L, "FULFILLED"), (line.FulfilledBaseQuantity, line.CancelledBaseQuantity, line.RemainingBaseQuantity, line.Status));
        Assert.Equal((0L, 0L), await BalanceAsync(env, early.Id));
        Assert.Equal((55L, 0L), await BalanceAsync(env, late.Id));

        var movement = await env.Context.StockMovements.AsNoTracking().Include(m => m.Items).SingleAsync(m => m.OrderId == order.Id, Token);
        Assert.Equal((StockMovementType.Sale, StockMovementStatus.Posted, env.Actor.Id), (movement.MovementType, movement.Status, movement.PostedBy));
        Assert.StartsWith("SM-", movement.MovementNumber);
        Assert.Equal(2, movement.Items.Count);
        var earlyItem = movement.Items.Single(i => i.InventoryLotId == early.Id);
        var lateItem = movement.Items.Single(i => i.InventoryLotId == late.Id);
        // Negative quantities, cost snapshot at the weighted average (5 000), the balance after the issue.
        Assert.Equal((-100L, 5_000m, 500_000m, 0L), (earlyItem.QuantityDeltaBase, earlyItem.UnitCostSnapshot, earlyItem.TotalCostSnapshot, earlyItem.QuantityOnHandAfter));
        Assert.Equal((-25L, 5_000m, 125_000m, 55L), (lateItem.QuantityDeltaBase, lateItem.UnitCostSnapshot, lateItem.TotalCostSnapshot, lateItem.QuantityOnHandAfter));

        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal("CONSUMED", reservation.Status);
        Assert.All(reservation.Items, i => Assert.Equal((i.ReservedBaseQuantity, 0L), (i.ConsumedBaseQuantity, i.RemainingBaseQuantity)));
        Assert.Contains(await env.Context.AuditLogs.AsNoTracking().Where(a => a.EntityId == order.Id).Select(a => a.Action).ToListAsync(Token), a => a == "ORDER_PICKED_UP");
    }

    [RealDbFact]
    public async Task A_partial_pickup_leaves_the_order_open_until_the_rest_is_handed_over()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var order = await ConfirmedOrderAsync(env, sku, 50);
        var itemId = order.Items.Single().Id;

        var first = await env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 20)), Token);

        Assert.Equal(("PARTIALLY_FULFILLED", null), (first.Status, first.PickupCompletedAt));
        Assert.Equal((20L, 30L, "PARTIALLY_FULFILLED"), (first.Items.Single().FulfilledBaseQuantity, first.Items.Single().RemainingBaseQuantity, first.Items.Single().Status));
        Assert.Equal((80L, 30L), await BalanceAsync(env, lot.Id));
        Assert.Equal("PARTIALLY_CONSUMED", (await env.Confirmation.GetReservationAsync(order.Id, Token)).Status);

        var second = await env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 30)), Token);

        Assert.Equal("COMPLETED", second.Status);
        Assert.NotNull(second.PickupCompletedAt);
        Assert.Equal((50L, 0L), await BalanceAsync(env, lot.Id));
        Assert.Equal("CONSUMED", (await env.Confirmation.GetReservationAsync(order.Id, Token)).Status);
        Assert.Equal(2, await env.Context.StockMovements.CountAsync(m => m.OrderId == order.Id, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 6)), Token));
    }

    [RealDbFact]
    public async Task Another_lot_than_the_reserved_one_takes_over_the_reservation_and_it_ends_consumed()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var reserved = await NewLotAsync(env, sku, "L-RESERVED", 100, Today.AddDays(30));
        var other = await NewLotAsync(env, sku, "L-OTHER", 100, Today.AddDays(60));
        var order = await ConfirmedOrderAsync(env, sku, 100);
        var itemId = order.Items.Single().Id;
        Assert.Equal((100L, 100L), await BalanceAsync(env, reserved.Id));

        // 60 come from the other lot: the reservation moves there (60 released in the reserved lot, 60 reserved and
        // consumed in the other one), and the stock leaves the other lot.
        var first = await env.Pickup.PickupAsync(order.Id, Take(itemId, (other.Id, 60)), Token);

        Assert.Equal("PARTIALLY_FULFILLED", first.Status);
        Assert.Equal((100L, 40L), await BalanceAsync(env, reserved.Id));
        Assert.Equal((40L, 0L), await BalanceAsync(env, other.Id));
        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal("PARTIALLY_CONSUMED", reservation.Status);
        var reservedLine = reservation.Items.Single(i => i.InventoryLotId == reserved.Id);
        var otherLine = reservation.Items.Single(i => i.InventoryLotId == other.Id);
        Assert.Equal((100L, 0L, 60L, 40L), (reservedLine.ReservedBaseQuantity, reservedLine.ConsumedBaseQuantity, reservedLine.ReleasedBaseQuantity, reservedLine.RemainingBaseQuantity));
        Assert.Equal((60L, 60L, 0L, 0L), (otherLine.ReservedBaseQuantity, otherLine.ConsumedBaseQuantity, otherLine.ReleasedBaseQuantity, otherLine.RemainingBaseQuantity));

        // Another 20 from the other lot again: its line there grows (one line per item and lot).
        await env.Pickup.PickupAsync(order.Id, Take(itemId, (other.Id, 20)), Token);
        var grown = (await env.Confirmation.GetReservationAsync(order.Id, Token)).Items.Single(i => i.InventoryLotId == other.Id);
        Assert.Equal((80L, 80L), (grown.ReservedBaseQuantity, grown.ConsumedBaseQuantity));
        Assert.Equal((100L, 20L), await BalanceAsync(env, reserved.Id));

        var last = await env.Pickup.PickupAsync(order.Id, Take(itemId, (reserved.Id, 20)), Token);

        Assert.Equal("COMPLETED", last.Status);
        Assert.Equal((80L, 0L), await BalanceAsync(env, reserved.Id));
        Assert.Equal((20L, 0L), await BalanceAsync(env, other.Id));
        Assert.Equal("CONSUMED", (await env.Confirmation.GetReservationAsync(order.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task An_order_handed_over_entirely_from_other_lots_still_ends_consumed()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var reserved = await NewLotAsync(env, sku, "L-RESERVED", 100, Today.AddDays(30));
        var other = await NewLotAsync(env, sku, "L-OTHER", 100, Today.AddDays(60));
        var order = await ConfirmedOrderAsync(env, sku, 100);

        var done = await env.Pickup.PickupAsync(order.Id, Take(order.Items.Single().Id, (other.Id, 100)), Token);

        Assert.Equal("COMPLETED", done.Status);
        Assert.Equal((100L, 0L), await BalanceAsync(env, reserved.Id));
        Assert.Equal((0L, 0L), await BalanceAsync(env, other.Id));
        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal("CONSUMED", reservation.Status);
        Assert.Equal((100L, 0L, 100L, 0L), (
            reservation.Items.Single(i => i.InventoryLotId == reserved.Id).ReservedBaseQuantity,
            reservation.Items.Single(i => i.InventoryLotId == reserved.Id).ConsumedBaseQuantity,
            reservation.Items.Single(i => i.InventoryLotId == reserved.Id).ReleasedBaseQuantity,
            reservation.Items.Single(i => i.InventoryLotId == reserved.Id).RemainingBaseQuantity));
        Assert.Equal(100L, reservation.Items.Single(i => i.InventoryLotId == other.Id).ConsumedBaseQuantity);
        var movement = await env.Context.StockMovements.AsNoTracking().Include(m => m.Items).SingleAsync(m => m.OrderId == order.Id, Token);
        Assert.Equal((-100L, 5_000m), (Assert.Single(movement.Items).QuantityDeltaBase, movement.Items.Single().UnitCostSnapshot));
    }

    [RealDbFact]
    public async Task A_lot_that_cannot_be_sold_or_is_not_the_items_is_refused_and_nothing_changes()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var other = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var spare = await NewLotAsync(env, sku, "L-SPARE", 100, Today.AddDays(60));
        var foreign = await NewLotAsync(env, other, "L-OTHER", 100, Today.AddDays(30));
        var order = await ConfirmedOrderAsync(env, sku, 10);
        var itemId = order.Items.Single().Id;

        Task<OrderResponse> Pickup(Guid lotId, long quantity = 10) => env.Pickup.PickupAsync(order.Id, Take(itemId, (lotId, quantity)), Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Pickup(foreign.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Pickup(Guid.NewGuid()));

        // The reserved lot was blocked after the reservation, and the spare one expires: neither can be issued.
        spare.ChangeStatus(InventoryLotStatus.Quarantined);
        lot.ChangeStatus(InventoryLotStatus.Blocked);
        await env.Context.SaveChangesAsync(Token);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => Pickup(lot.Id));
        Assert.Contains("cannot be sold", refused.Errors!["items[0]"].Single());
        await Assert.ThrowsAsync<BusinessRuleException>(() => Pickup(spare.Id));

        Assert.Equal((100L, 10L), await BalanceAsync(env, lot.Id));
        Assert.Equal((100L, 0L), await BalanceAsync(env, spare.Id));
        Assert.Equal("CONFIRMED", (await env.Orders.GetAsync(order.Id, Token)).Status);
        Assert.Empty(await env.Context.StockMovements.AsNoTracking().Where(m => m.OrderId == order.Id).ToListAsync(Token));
    }

    [RealDbFact]
    public async Task Stock_reserved_for_another_order_cannot_be_taken_from_another_lot()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var first = await NewLotAsync(env, sku, "L-FIRST", 100, Today.AddDays(30));
        var second = await NewLotAsync(env, sku, "L-SECOND", 50, Today.AddDays(60));
        var mine = await ConfirmedOrderAsync(env, sku, 40);
        var rival = await ConfirmedOrderAsync(env, sku, 110);
        var itemId = mine.Items.Single().Id;
        // mine holds 40 of the first lot; the rival holds 60 of it and 50 of the second.
        Assert.Equal((100L, 100L), await BalanceAsync(env, first.Id));
        Assert.Equal((50L, 50L), await BalanceAsync(env, second.Id));

        // Nothing of the second lot is free for me.
        await Assert.ThrowsAsync<DomainException>(() => env.Pickup.PickupAsync(mine.Id, Take(itemId, (second.Id, 10)), Token));

        Assert.Equal((50L, 50L), await BalanceAsync(env, second.Id));
        Assert.Equal((100L, 100L), await BalanceAsync(env, first.Id));
    }

    [RealDbFact]
    public async Task Quantities_must_be_whole_packages_within_what_is_left_and_the_order_must_be_ready_for_pickup()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var order = await ConfirmedOrderAsync(env, sku, 2, box: true);
        var itemId = order.Items.Single().Id;
        Assert.Equal(12L, order.Items.Single().BaseQuantity);

        var notWhole = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 5)), Token));
        Assert.Contains("whole packages", notWhole.Errors!["items[0]"].Single());
        var tooMany = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 18)), Token));
        Assert.Contains("exceed", tooMany.Errors!["items[0]"].Single());
        var unknown = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(order.Id, Take(Guid.NewGuid(), (lot.Id, 6)), Token));
        Assert.Single(unknown.Errors!);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Pickup.PickupAsync(Guid.NewGuid(), Take(itemId, (lot.Id, 6)), Token));

        // One box of 6 is fine and leaves the order partly fulfilled.
        var one = await env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 6)), Token);
        Assert.Equal(("PARTIALLY_FULFILLED", 6L), (one.Status, one.Items.Single().RemainingBaseQuantity));

        // A PENDING order and a DELIVERY order are not handed over at the counter.
        var pending = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "PICKUP", [new OrderItemRequest(sku.StoreProductId, sku.BottleId, 1)]), Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(pending.Id, Take(pending.Items.Single().Id, (lot.Id, 1)), Token));
        var delivery = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "WALK_IN", "FULL_PAYMENT", "DELIVERY", [new OrderItemRequest(sku.StoreProductId, sku.BottleId, 1)], DeliveryAddress: new DeliveryAddressRequest("Chú Tư", "0912345678", "Ấp 3", "Cần Thơ")),
            Token);
        await env.Confirmation.ConfirmAsync(delivery.Id, Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.PickupAsync(delivery.Id, Take(delivery.Items.Single().Id, (lot.Id, 1)), Token));
    }

    [RealDbFact]
    public async Task A_line_of_boxes_split_between_lots_at_any_base_quantity_is_handed_over_as_reserved()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));
        // 21 boxes of 6 = 126 base units: FEFO reserves 100 + 26, neither a whole number of boxes on its own.
        var order = await ConfirmedOrderAsync(env, sku, 21, box: true);
        var itemId = order.Items.Single().Id;
        Assert.Equal((100L, 100L), await BalanceAsync(env, early.Id));
        Assert.Equal((80L, 26L), await BalanceAsync(env, late.Id));

        var done = await env.Pickup.PickupAsync(
            order.Id, new PickupRequest([new PickupItemRequest(itemId, [new PickupLotRequest(early.Id, 100), new PickupLotRequest(late.Id, 26)])]), Token);

        Assert.Equal("COMPLETED", done.Status);
        Assert.Equal((0L, 0L), await BalanceAsync(env, early.Id));
        Assert.Equal((54L, 0L), await BalanceAsync(env, late.Id));
        Assert.Equal("CONSUMED", (await env.Confirmation.GetReservationAsync(order.Id, Token)).Status);
    }

    // ----- Cancel remaining -----

    [RealDbFact]
    public async Task Cancelling_the_rest_of_a_partly_picked_up_order_ends_it_and_releases_the_reservation()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var order = await ConfirmedOrderAsync(env, sku, 50);
        var itemId = order.Items.Single().Id;
        await env.Pickup.PickupAsync(order.Id, Take(itemId, (lot.Id, 20)), Token);

        var ended = await env.Pickup.CancelRemainingAsync(order.Id, itemId, new CancelRemainingRequest(" Khách không lấy nữa "), Token);

        Assert.Equal(("PARTIALLY_CANCELLED", "Khách không lấy nữa"), (ended.Status, ended.CancelReason));
        Assert.NotNull(ended.CompletedAt);
        var line = ended.Items.Single();
        Assert.Equal((20L, 30L, 0L, "PARTIALLY_CANCELLED"), (line.FulfilledBaseQuantity, line.CancelledBaseQuantity, line.RemainingBaseQuantity, line.Status));
        // No stock movement: the 30 only stop being reserved.
        Assert.Equal((80L, 0L), await BalanceAsync(env, lot.Id));
        Assert.Single(await env.Context.StockMovements.AsNoTracking().Where(m => m.OrderId == order.Id).ToListAsync(Token));
        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal("CONSUMED", reservation.Status);
        var reserved = Assert.Single(reservation.Items);
        Assert.Equal((20L, 30L, 0L), (reserved.ConsumedBaseQuantity, reserved.ReleasedBaseQuantity, reserved.RemainingBaseQuantity));
        Assert.Equal("Khách không lấy nữa", (await env.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == order.Id && a.Action == "ORDER_ITEM_REMAINING_CANCELLED", Token)).Reason);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Pickup.CancelRemainingAsync(order.Id, itemId, new CancelRemainingRequest("lại"), Token));
    }

    [RealDbFact]
    public async Task Cancelling_an_untouched_line_cancels_the_order_and_other_lines_keep_it_open()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var other = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var otherLot = await NewLotAsync(env, other, "L2", 100, Today.AddDays(30));

        var single = await ConfirmedOrderAsync(env, sku, 10);
        var cancelled = await env.Pickup.CancelRemainingAsync(single.Id, single.Items.Single().Id, new CancelRemainingRequest("Đặt nhầm"), Token);
        Assert.Equal(("CANCELLED", "CANCELLED", "Đặt nhầm"), (cancelled.Status, cancelled.Items.Single().Status, cancelled.CancelReason));
        Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
        Assert.Equal("RELEASED", (await env.Confirmation.GetReservationAsync(single.Id, Token)).Status);

        var two = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                "WALK_IN", "FULL_PAYMENT", "PICKUP",
                [new OrderItemRequest(sku.StoreProductId, sku.BottleId, 10), new OrderItemRequest(other.StoreProductId, other.BottleId, 5)]),
            Token);
        await env.Confirmation.ConfirmAsync(two.Id, Token);
        var firstItem = two.Items.Single(i => i.StoreProductId == sku.StoreProductId).Id;
        var secondItem = two.Items.Single(i => i.StoreProductId == other.StoreProductId).Id;

        var partial = await env.Pickup.CancelRemainingAsync(two.Id, firstItem, new CancelRemainingRequest("Hết nhu cầu"), Token);
        Assert.Equal("CONFIRMED", partial.Status);
        Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
        Assert.Equal((100L, 5L), await BalanceAsync(env, otherLot.Id));

        // Handing over the other line ends the order: one line cancelled, one fulfilled.
        var done = await env.Pickup.PickupAsync(two.Id, Take(secondItem, (otherLot.Id, 5)), Token);
        Assert.Equal("PARTIALLY_CANCELLED", done.Status);
        Assert.NotNull(done.PickupCompletedAt);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Pickup.CancelRemainingAsync(two.Id, Guid.NewGuid(), new CancelRemainingRequest("x"), Token));
    }
}
