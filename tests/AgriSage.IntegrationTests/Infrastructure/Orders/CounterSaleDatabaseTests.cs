using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Payments.Enums;
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

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): the quick counter sale (F1.7), always rolled back. Active walk-in lists
// already in agrisage-dev are switched off inside the transaction first. After a refused sale the change tracker is
// cleared, as a new request would get a new context: the refused attempt must have saved nothing.
[Collection(RealDb.WalkInPriceListCollection)]
public class CounterSaleDatabaseTests
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
            Ledger = new OrderPrepaymentLedger(context);
            var queries = new OrderQueries(context);
            Sales = new CounterSaleService(
                context,
                new OrderBuilder(context, new PriceResolver(context), user, clock, audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(context, clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy()))),
                new PaymentAllocator(clock, new StubCreditReservationAdjuster(), new StubDebtRepaymentPosting()),
                new OrderConfirmer(context, locks, new StubOrderSettlementGuard(), user, clock, audit),
                new FulfillmentPostingService(context, locks, new StubFulfillmentFinancialPosting(Ledger), new OrderPaymentCancellation(context, locks, new FakePaymentGateway(), clock, audit)),
                queries,
                new PaymentQueries(context),
                user,
                clock,
                errors,
                audit);
            Confirmation = new OrderConfirmationService(
                context, locks, new OrderConfirmer(context, locks, new StubOrderSettlementGuard(), user, clock, audit), queries, clock, errors, audit);
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderPrepaymentLedger Ledger { get; }

        public CounterSaleService Sales { get; }

        public OrderConfirmationService Confirmation { get; }

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
        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Sale Tester", "hash", $"{Tag()}@example.test", null);
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

    private static CounterSaleItemRequest Line(
        Sku sku, long quantity, bool box = false, decimal? price = null, string? reason = null, params (Guid LotId, long Quantity)[] lots) =>
        new(sku.StoreProductId, box ? sku.BoxId : sku.BottleId, quantity, price, reason,
            lots.Length == 0 ? null : lots.Select(l => new CounterSaleLotRequest(l.LotId, l.Quantity)).ToList());

    private static CounterSaleRequest Walk(params CounterSaleItemRequest[] items) => new("WALK_IN", items);

    private static async Task<(long OnHand, long Reserved)> BalanceAsync(Env env, Guid lotId)
    {
        var balance = await env.Context.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == lotId, Token);

        return (balance.QuantityOnHand, balance.QuantityReserved);
    }

    // Rows a sale would leave behind, by the actor of the test (the test database is shared, so only ours count).
    private static async Task<(int Orders, int Payments, int Movements, int Reservations)> RowsAsync(Env env) =>
        (await env.Context.Orders.IgnoreQueryFilters().CountAsync(o => o.CreatedBy == env.Actor.Id, Token),
            await env.Context.Payments.IgnoreQueryFilters().CountAsync(p => p.CreatedBy == env.Actor.Id, Token),
            await env.Context.StockMovements.IgnoreQueryFilters().CountAsync(m => m.CreatedBy == env.Actor.Id, Token),
            await env.Context.InventoryReservations.IgnoreQueryFilters().CountAsync(r => r.ReservedBy == env.Actor.Id, Token));

    // ----- Preview -----

    [RealDbFact]
    public async Task The_preview_prices_the_lines_and_proposes_fefo_lots_and_saves_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var scarce = await NewSkuAsync(env);
        await NewLotAsync(env, scarce, "L-S", 4, Today.AddDays(30));

        var preview = await env.Sales.PreviewAsync(
            Walk(Line(sku, 21, box: true, price: 50_000m, reason: "Khách quen"), Line(sku, 3), Line(scarce, 10)), Token);

        Assert.Equal(env.WalkInListId, preview.PriceListId);
        Assert.Null(preview.CustomerGroupId);
        // 21 boxes × 50 000 (overridden) + 3 × 10 000 + 10 × 10 000
        Assert.Equal(1_180_000m, preview.TotalAmount);
        var boxes = preview.Items[0];
        Assert.Equal((sku.Code, 21L, 6L, 126L, 55_000m, 50_000m, 1_050_000m), (boxes.Sku, boxes.Quantity, boxes.ConversionToBase, boxes.BaseQuantity, boxes.SuggestedUnitPrice, boxes.UnitPrice, boxes.LineTotalAmount));
        // 126 base units: the early lot (100) first, then 26 of the late one; FEFO answers each line against the free stock.
        Assert.Equal([early.Id, late.Id], boxes.Lots.Select(l => l.InventoryLotId));
        Assert.Equal([(100L, 100L), (80L, 26L)], boxes.Lots.Select(l => (l.AvailableBaseQuantity, l.SuggestedBaseQuantity)));
        Assert.Equal(0L, boxes.ShortageBaseQuantity);
        Assert.Equal((3L, 10L), (preview.Items[1].BaseQuantity, preview.Items[2].BaseQuantity));
        Assert.Equal((6L, 0L), (preview.Items[2].ShortageBaseQuantity, preview.Items[2].Lots.Sum(l => l.SuggestedBaseQuantity) - 4L));

        Assert.Equal((0, 0, 0, 0), await RowsAsync(env));
        Assert.Equal((100L, 0L), await BalanceAsync(env, early.Id));
        Assert.Empty(env.Context.Orders.Local);

        // An unexplained price change is refused like on an order.
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Sales.PreviewAsync(Walk(Line(sku, 1, price: 1m)), Token));
        Assert.Contains("reason", refused.Errors!["items[0]"].Single());
    }

    // ----- Selling -----

    [RealDbFact]
    public async Task A_sale_completes_the_order_pays_it_and_moves_the_stock_in_one_go()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var early = await NewLotAsync(env, sku, "L-EARLY", 100, Today.AddDays(30));
        var late = await NewLotAsync(env, sku, "L-LATE", 80, Today.AddDays(90));

        var result = await env.Sales.SellAsync(
            Walk(
                Line(sku, 21, box: true, price: 50_000m, reason: "Khách quen", lots: [(early.Id, 100), (late.Id, 26)]),
                Line(sku, 3, lots: [(late.Id, 3)])),
            Token);

        var order = result.Order;
        var payment = result.Payment;
        Assert.Equal(("COMPLETED", "COUNTER", "WALK_IN", "FULL_PAYMENT", "PICKUP", "Khách lẻ"),
            (order.Status, order.Source, order.CustomerType, order.SettlementType, order.FulfillmentType, order.CustomerName));
        Assert.Equal((1_080_000m, env.Actor.Id), (order.TotalAmount, order.PickupCompletedBy));
        Assert.NotNull(order.PickupCompletedAt);
        Assert.All(order.Items, i => Assert.Equal(("FULFILLED", 0L), (i.Status, i.RemainingBaseQuantity)));
        Assert.True(order.Items[0].PriceOverridden);

        // The payment is PAID for the total, allocated to the order and fully consumed by the handover.
        Assert.Equal(("CASH", "PAID", "STAFF", 1_080_000m, 0m), (payment.PaymentMethod, payment.Status, payment.ConfirmationSource, payment.Amount, payment.UnallocatedAmount));
        var allocation = Assert.Single(payment.Allocations);
        Assert.Equal((order.Id, 1_080_000m, 1_080_000m, "ACTIVE"), (allocation.OrderId, allocation.AllocatedAmount, allocation.PrepaymentConsumedAmount, allocation.Status));
        var summary = await new PaymentQueries(env.Context).GetOrderSummaryAsync(order.Id, null, Token);
        Assert.Equal((1_080_000m, 0m, 1_080_000m, 0m), (summary.PaidAmount, summary.AvailablePrepayment, summary.ConsumedPrepayment, summary.RemainingToPay));

        // Stock left the lots (129 = 126 + 3 base units), nothing stays reserved, one SALE movement is posted.
        Assert.Equal((0L, 0L), await BalanceAsync(env, early.Id));
        Assert.Equal((51L, 0L), await BalanceAsync(env, late.Id));
        var movement = await env.Context.StockMovements.AsNoTracking().Include(m => m.Items).SingleAsync(m => m.OrderId == order.Id, Token);
        Assert.Equal((StockMovementType.Sale, StockMovementStatus.Posted), (movement.MovementType, movement.Status));
        Assert.Equal(-129L, movement.Items.Sum(i => i.QuantityDeltaBase));
        Assert.All(movement.Items, i => Assert.Equal(5_000m, i.UnitCostSnapshot));
        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal("CONSUMED", reservation.Status);
        Assert.All(reservation.Items, i => Assert.Equal(0L, i.RemainingBaseQuantity));

        var actions = await env.Context.AuditLogs.AsNoTracking().Where(a => a.EntityId == order.Id || a.EntityId == payment.Id).Select(a => a.Action).ToListAsync(Token);
        Assert.Contains("ORDER_CONFIRMED", actions);
        Assert.Contains("COUNTER_SALE_COMPLETED", actions);
        Assert.Contains("PAYMENT_RECEIVED", actions);
        Assert.Contains("PRICE_OVERRIDE", actions);
        Assert.Equal((1, 1, 1, 1), await RowsAsync(env));
    }

    [RealDbFact]
    public async Task A_registered_customer_is_the_payer_and_takes_the_group_price()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var farmerUser = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), "Nông dân quen", "hash", $"{Tag()}@example.test", "09" + Random.Shared.Next(10_000_000, 99_999_999));
        var farmer = new FarmerProfile(farmerUser.Id);
        env.Context.AddRange(farmerUser, farmer);
        await env.Context.SaveChangesAsync(Token);

        var result = await env.Sales.SellAsync(
            new CounterSaleRequest("REGISTERED", [Line(sku, 5, lots: [(lot.Id, 5)])], farmer.Id, "Bỏ qua", "0999999999"),
            Token);

        Assert.Equal(("Nông dân quen", farmer.Id, "COMPLETED"), (result.Order.CustomerName, result.Order.FarmerProfileId, result.Order.Status));
        Assert.Equal((farmer.Id, "Nông dân quen"), (result.Payment.PayerFarmerProfileId, result.Payment.PayerName));
    }

    // ----- Atomicity: any refusal leaves nothing -----

    [RealDbFact]
    public async Task A_lot_blocked_between_preview_and_sale_refuses_it_and_leaves_nothing_behind()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var request = Walk(Line(sku, 10, lots: [(lot.Id, 10)]));
        var preview = await env.Sales.PreviewAsync(request, Token);
        Assert.Equal(lot.Id, Assert.Single(preview.Items[0].Lots).InventoryLotId);

        lot.ChangeStatus(InventoryLotStatus.Blocked);
        await env.Context.SaveChangesAsync(Token);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Sales.SellAsync(request, Token));

        Assert.Contains("short", refused.Errors!["items[0]"].Single());
        env.Context.ChangeTracker.Clear();
        Assert.Equal((0, 0, 0, 0), await RowsAsync(env));
        Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
    }

    [RealDbFact]
    public async Task Lots_that_do_not_match_the_lines_or_a_missing_lots_list_save_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var other = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100, Today.AddDays(30));
        var small = await NewLotAsync(env, sku, "L-SMALL", 4, Today.AddDays(60));
        var foreign = await NewLotAsync(env, other, "L-OTHER", 100, Today.AddDays(30));

        async Task AssertRefusedAsync(CounterSaleRequest request)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => env.Sales.SellAsync(request, Token));
            env.Context.ChangeTracker.Clear();
            Assert.Equal((0, 0, 0, 0), await RowsAsync(env));
            Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
        }

        // Not whole boxes, too little, too much, another product's lot, more than the lot holds.
        await AssertRefusedAsync(Walk(Line(sku, 2, box: true, lots: [(lot.Id, 11)])));
        await AssertRefusedAsync(Walk(Line(sku, 10, lots: [(lot.Id, 9)])));
        await AssertRefusedAsync(Walk(Line(sku, 10, lots: [(lot.Id, 11)])));
        await AssertRefusedAsync(Walk(Line(sku, 10, lots: [(foreign.Id, 10)])));
        await AssertRefusedAsync(Walk(Line(sku, 10, lots: [(small.Id, 10)])));
        await AssertRefusedAsync(Walk(Line(sku, 10)));
        var missing = await Assert.ThrowsAsync<ValidationException>(() => env.Sales.SellAsync(Walk(Line(sku, 10, lots: [(lot.Id, 10)]), Line(other, 1)), Token));
        Assert.Equal(["items[1].lots"], missing.Errors.Keys);
        env.Context.ChangeTracker.Clear();

        // The same lot named twice counts as one pick of the sum.
        var twice = await env.Sales.SellAsync(Walk(Line(sku, 10, lots: [(lot.Id, 4), (lot.Id, 6)])), Token);
        Assert.Equal("COMPLETED", twice.Order.Status);
        Assert.Equal((90L, 0L), await BalanceAsync(env, lot.Id));
    }
}
