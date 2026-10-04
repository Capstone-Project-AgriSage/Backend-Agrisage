using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Orders;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): cancelling an order, OrderCanceller and the real
// IOrderPaymentCancellation (F1.6), always rolled back. Active walk-in lists already in agrisage-dev are switched off
// inside the transaction first.
[Collection(RealDb.WalkInPriceListCollection)]
public class OrderCancellationDatabaseTests
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
                context, new OrderBuilder(context, new PriceResolver(context), user, clock, audit), queries, user, clock, errors, audit);
            Confirmation = new OrderConfirmationService(
                context, locks, new OrderConfirmer(context, locks, new TemporaryOrderSettlementGuard(), user, clock, audit), queries, clock, errors, audit);
            Cancellation = new OrderPaymentCancellation(context, locks, clock, audit);
            Pickup = new OrderPickupService(
                context, locks, new FulfillmentPostingService(context, locks, new TemporaryFulfillmentFinancialPosting(new OrderPrepaymentLedger(context))),
                new TemporaryOrderSettlementGuard(), Cancellation, queries, user, clock, audit);
            Canceller = new OrderCancellationService(
                context, locks, new OrderCanceller(context, locks, new TemporaryOrderSettlementGuard(), Cancellation), queries, user, clock, errors, audit);
            Ledger = new OrderPrepaymentLedger(context);
            var paymentQueries = new PaymentQueries(context);
            Payments = new PaymentService(
                context, locks, Ledger,
                new PaymentAllocator(clock, new TemporaryCreditReservationAdjuster(), new TemporaryDebtRepaymentPosting()),
                paymentQueries, user, clock, errors, audit);
            PaymentReads = paymentQueries;
        }

        public AgriSageDbContext Context { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderService Orders { get; }

        public OrderConfirmationService Confirmation { get; }

        public OrderPaymentCancellation Cancellation { get; }

        public OrderPickupService Pickup { get; }

        public OrderCancellationService Canceller { get; }

        public OrderPrepaymentLedger Ledger { get; }

        public PaymentService Payments { get; }

        public PaymentQueries PaymentReads { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid WalkInListId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record Sku(Guid StoreProductId, Guid BottleId);

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

        var unit = await context.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Code == "BOTTLE", Token);
        if (unit is null)
        {
            unit = new Unit("BOTTLE", "Bottle");
            context.Units.Add(unit);
            await context.SaveChangesAsync(Token);
        }

        env.Bottle = unit.Id;
        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Cancel Tester", "hash", $"{Tag()}@example.test", null);
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

    // A bottle product priced 10 000 in the walk-in list.
    private static async Task<Sku> NewSkuAsync(Env env)
    {
        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        var product = await env.Products.CreateAsync(
            new CreateProductRequest($"SKU-{tag}", $"Product {tag}", category.Id, [new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle")]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        var bottleId = product.Packagings.Single().Id;
        await env.PriceLists.UpsertItemsAsync(
            env.WalkInListId, new UpsertPriceListItemsRequest([new PriceListItemInput(storeProduct.Id, bottleId, 10_000m)]), Token);

        return new Sku(storeProduct.Id, bottleId);
    }

    private static async Task<InventoryLot> NewLotAsync(Env env, Sku sku, string number, long onHand)
    {
        var lot = new InventoryLot(sku.StoreProductId, number, null, Today.AddDays(30));
        lot.ReceiveStock(onHand, 5_000m);
        env.Context.InventoryLots.Add(lot);
        await env.Context.SaveChangesAsync(Token);

        return lot;
    }

    private static Task<OrderResponse> NewOrderAsync(Env env, Sku sku, long quantity) =>
        env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "PICKUP", [new OrderItemRequest(sku.StoreProductId, sku.BottleId, quantity)]),
            Token);

    private static Task<PaymentResponse> PayAsync(Env env, Guid orderId, decimal amount) =>
        env.Payments.ReceiveCashAsync(new CashPaymentRequest("ORDER_PAYMENT", amount, orderId), Token);

    private static async Task<(long OnHand, long Reserved)> BalanceAsync(Env env, Guid lotId)
    {
        var balance = await env.Context.InventoryLotBalances.AsNoTracking().SingleAsync(b => b.InventoryLotId == lotId, Token);

        return (balance.QuantityOnHand, balance.QuantityReserved);
    }

    private static PickupRequest Take(Guid orderItemId, Guid lotId, long quantity) =>
        new([new PickupItemRequest(orderItemId, [new PickupLotRequest(lotId, quantity)])]);

    private static async Task<PaymentAllocation> AllocationAsync(Env env, Guid paymentId) =>
        await env.Context.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == paymentId, Token);

    // ----- Cancelling a whole order -----

    [RealDbFact]
    public async Task An_unpaid_order_is_cancelled_with_its_reason_and_nothing_to_hand_back()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var order = await NewOrderAsync(env, sku, 5);

        var result = await env.Canceller.CancelAsync(order.Id, new CancelOrderRequest(" Khách đổi ý "), Token);

        Assert.Equal(("CANCELLED", "Khách đổi ý", env.Actor.Id), (result.Order.Status, result.Order.CancelReason, result.Order.CancelledBy));
        Assert.NotNull(result.Order.CancelledAt);
        Assert.All(result.Order.Items, i => Assert.Equal(("CANCELLED", 0L), (i.Status, i.RemainingBaseQuantity)));
        Assert.Empty(result.Refunds);
        var audit = await env.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == order.Id && a.Action == "ORDER_CANCELLED", Token);
        Assert.Equal(("Khách đổi ý", env.Actor.Id), (audit.Reason, audit.ActorUserId));

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("lại"), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Canceller.CancelAsync(Guid.NewGuid(), new CancelOrderRequest("x"), Token));
    }

    [RealDbFact]
    public async Task A_confirmed_order_gives_its_reserved_stock_back()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 30);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        Assert.Equal((100L, 30L), await BalanceAsync(env, lot.Id));

        var result = await env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("Hết nhu cầu"), Token);

        Assert.Equal("CANCELLED", result.Order.Status);
        Assert.Equal((100L, 0L), await BalanceAsync(env, lot.Id));
        var reservation = await env.Confirmation.GetReservationAsync(order.Id, Token);
        Assert.Equal(("CANCELLED", "Hết nhu cầu"), (reservation.Status, reservation.ReleaseReason));
        Assert.All(reservation.Items, i => Assert.Equal((0L, i.ReservedBaseQuantity), (i.RemainingBaseQuantity, i.ReleasedBaseQuantity)));
        Assert.Empty(await env.Context.StockMovements.AsNoTracking().Where(m => m.OrderId == order.Id).ToListAsync(Token));
    }

    [RealDbFact]
    public async Task A_paid_order_is_cancelled_with_one_pending_refund_per_payment()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var order = await NewOrderAsync(env, sku, 10);
        var first = await PayAsync(env, order.Id, 60_000m);
        var second = await PayAsync(env, order.Id, 40_000m);

        var result = await env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("Hết hàng"), Token);

        Assert.Equal("CANCELLED", result.Order.Status);
        Assert.Equal(2, result.Refunds.Count);
        Assert.Equal([60_000m, 40_000m], result.Refunds.Select(r => r.Amount));
        Assert.Equal([first.Id, second.Id], result.Refunds.Select(r => r.PaymentId));
        Assert.All(result.Refunds, r => Assert.Equal("CASH", r.RefundMethod));
        var day = BusinessCalendar.Today(Now);
        var numbers = result.Refunds.Select(r => DocumentNumbers.SequenceOf(r.RefundNumber, DocumentNumbers.Refund, day)).ToList();
        Assert.Equal([numbers[0], numbers[0] + 1], numbers);
        Assert.True(numbers[0] > 0);

        // The allocations are reversed (nothing was consumed), so nothing is "paid" for the order any more.
        Assert.Equal(PaymentAllocationStatus.Reversed, (await AllocationAsync(env, first.Id)).Status);
        Assert.Equal(PaymentAllocationStatus.Reversed, (await AllocationAsync(env, second.Id)).Status);
        var summary = await env.Payments.GetOrderSummaryAsync(order.Id, Token);
        Assert.Equal((0m, 0m), (summary.PaidAmount, summary.AvailablePrepayment));
        Assert.Equal(2, summary.Refunds.Count);
        Assert.All(summary.Refunds, r => Assert.Equal(("ORDER", "PENDING", order.Id), (r.Source, r.Status, r.OrderId)));
        Assert.Equal([60_000m, 40_000m], summary.Refunds.OrderBy(r => r.RefundNumber).Select(r => r.Amount));
        Assert.Equal(2, (await env.Context.AuditLogs.AsNoTracking().Where(a => a.Action == "ORDER_PREPAYMENT_RELEASED" && (a.EntityId == first.Id || a.EntityId == second.Id)).ToListAsync(Token)).Count);

        // The order is closed: no more payments.
        await Assert.ThrowsAsync<BusinessRuleException>(() => PayAsync(env, order.Id, 1m));
    }

    [RealDbFact]
    public async Task A_partly_handed_over_order_cannot_be_cancelled_as_a_whole()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 10);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        await env.Pickup.PickupAsync(order.Id, Take(order.Items.Single().Id, lot.Id, 4), Token);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("x"), Token));

        Assert.Contains("cancel the rest", refused.Message);
        Assert.Equal("PARTIALLY_FULFILLED", (await env.Orders.GetAsync(order.Id, Token)).Status);
        Assert.Equal((96L, 6L), await BalanceAsync(env, lot.Id));
    }

    [RealDbFact]
    public async Task Pending_cash_payments_are_cancelled_and_a_pending_payos_payment_stops_the_cancellation()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var cash = await NewOrderAsync(env, sku, 1);
        var online = await NewOrderAsync(env, sku, 1);
        var pendingCash = new Payment(env.StoreId, "PM-PENDING-C", PaymentContext.OrderPayment, PaymentMethod.Cash, 10_000m, Now, null, env.Actor.Id, orderId: cash.Id);
        var pendingOnline = new Payment(env.StoreId, "PM-PENDING-O", PaymentContext.OrderPayment, PaymentMethod.PayOs, 10_000m, Now, null, env.Actor.Id, orderId: online.Id);
        env.Context.Payments.AddRange(pendingCash, pendingOnline);
        await env.Context.SaveChangesAsync(Token);

        var done = await env.Canceller.CancelAsync(cash.Id, new CancelOrderRequest("Huỷ"), Token);
        Assert.Equal(("CANCELLED", "CANCELLED"), (done.Order.Status, (await env.Payments.GetAsync(pendingCash.Id, Token)).Status));
        Assert.Empty(done.Refunds);

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Canceller.CancelAsync(online.Id, new CancelOrderRequest("Huỷ"), Token));
        Assert.Contains("payOS", refused.Message);
        Assert.Equal(("PENDING_CONFIRMATION", "PENDING"), ((await env.Orders.GetAsync(online.Id, Token)).Status, (await env.Payments.GetAsync(pendingOnline.Id, Token)).Status));
    }

    // ----- Cancelling the rest after a partial handover (cancel-remaining) -----

    [RealDbFact]
    public async Task Cancelling_the_rest_of_a_partly_delivered_paid_order_refunds_only_what_was_not_delivered()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 50); // 500 000
        var itemId = order.Items.Single().Id;
        var first = await PayAsync(env, order.Id, 300_000m);
        var second = await PayAsync(env, order.Id, 200_000m);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        await env.Pickup.PickupAsync(order.Id, Take(itemId, lot.Id, 20), Token); // delivered value 200 000

        var ended = await env.Pickup.CancelRemainingAsync(order.Id, itemId, new CancelRemainingRequest("Khách không lấy nữa"), Token);

        Assert.Equal("PARTIALLY_CANCELLED", ended.Status);
        // 300 000 comes back: the newest payment (200 000) entirely, 100 000 of the older one.
        var refunds = await env.Context.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).OrderBy(r => r.RefundNumber).ToListAsync(Token);
        Assert.Equal([(first.Id, 100_000m), (second.Id, 200_000m)], refunds.Select(r => (r.OriginalPaymentId!.Value, r.Amount)));
        Assert.All(refunds, r => Assert.Equal((RefundStatus.Pending, RefundMethod.Cash, env.Actor.Id), (r.Status, r.RefundMethod, r.RequestedBy)));
        var kept = await AllocationAsync(env, first.Id);
        Assert.Equal((200_000m, PaymentAllocationStatus.Active), (kept.AllocatedAmount, kept.Status));
        Assert.Equal(PaymentAllocationStatus.Reversed, (await AllocationAsync(env, second.Id)).Status);
        var summary = await env.Payments.GetOrderSummaryAsync(order.Id, Token);
        Assert.Equal((200_000m, 2), (summary.PaidAmount, summary.Refunds.Count));
        // The money given back is unallocated, not lost from the payment.
        Assert.Equal(100_000m, (await env.Payments.GetAsync(first.Id, Token)).UnallocatedAmount);
        Assert.Equal((80L, 0L), await BalanceAsync(env, lot.Id));
    }

    [RealDbFact]
    public async Task Prepayment_already_consumed_is_never_given_back()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 50);
        var itemId = order.Items.Single().Id;
        var payment = await PayAsync(env, order.Id, 500_000m);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        await env.Pickup.PickupAsync(order.Id, Take(itemId, lot.Id, 20), Token);

        // The handover consumed 200 000 (the value delivered). Another 50 000 are consumed, which is more than the delivered
        // value, so only 250 000 can go back.
        Assert.Equal(50_000m, await env.Ledger.ConsumeAsync(order.Id, 50_000m, Token));
        await env.Context.SaveChangesAsync(Token);
        await env.Pickup.CancelRemainingAsync(order.Id, itemId, new CancelRemainingRequest("Khách không lấy nữa"), Token);

        var refund = Assert.Single(await env.Context.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(Token));
        Assert.Equal((payment.Id, 250_000m), (refund.OriginalPaymentId, refund.Amount));
        var allocation = await AllocationAsync(env, payment.Id);
        Assert.Equal((250_000m, 250_000m, PaymentAllocationStatus.Active), (allocation.AllocatedAmount, allocation.PrepaymentConsumedAmount, allocation.Status));
    }

    [RealDbFact]
    public async Task A_prepayment_that_does_not_exceed_what_was_delivered_gives_nothing_back_and_a_second_call_changes_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 50);
        var itemId = order.Items.Single().Id;
        await PayAsync(env, order.Id, 150_000m);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        await env.Pickup.PickupAsync(order.Id, Take(itemId, lot.Id, 20), Token); // 200 000 delivered, 150 000 paid

        await env.Pickup.CancelRemainingAsync(order.Id, itemId, new CancelRemainingRequest("Khách không lấy nữa"), Token);

        Assert.Empty(await env.Context.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(Token));
        Assert.Equal(150_000m, (await env.Payments.GetOrderSummaryAsync(order.Id, Token)).PaidAmount);

        // Asking again (e.g. a retried call) finds nothing more to give back.
        var order2 = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id, Token);
        Assert.Empty(await env.Cancellation.ReverseForCancelledOrderAsync(order2, env.Actor.Id, "again", Token));
    }

    [RealDbFact]
    public async Task A_repeated_cancellation_call_does_not_request_the_refund_twice()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var order = await NewOrderAsync(env, sku, 10);
        await PayAsync(env, order.Id, 100_000m);

        var done = await env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("Huỷ"), Token);
        var entity = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id, Token);
        var again = await env.Cancellation.ReverseForCancelledOrderAsync(entity, env.Actor.Id, "Huỷ", Token);

        Assert.Single(done.Refunds);
        Assert.Empty(again);
        Assert.Single(await env.Context.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(Token));
    }

    [RealDbFact]
    public async Task A_completed_order_cannot_be_cancelled_and_nothing_changes()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sku = await NewSkuAsync(env);
        var lot = await NewLotAsync(env, sku, "L1", 100);
        var order = await NewOrderAsync(env, sku, 10);
        await env.Confirmation.ConfirmAsync(order.Id, Token);
        await env.Pickup.PickupAsync(order.Id, Take(order.Items.Single().Id, lot.Id, 10), Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Canceller.CancelAsync(order.Id, new CancelOrderRequest("muộn"), Token));

        Assert.Equal("COMPLETED", (await env.Orders.GetAsync(order.Id, Token)).Status);
        Assert.Empty(await env.Context.Refunds.AsNoTracking().Where(r => r.OrderId == order.Id).ToListAsync(Token));
    }
}
