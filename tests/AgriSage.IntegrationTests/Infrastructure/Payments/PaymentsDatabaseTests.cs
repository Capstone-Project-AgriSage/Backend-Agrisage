using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Services;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Payments;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): cash payments, payment queries, the real order prepayment ledger and
// the PaymentAllocator step (F1.3), always rolled back. Active walk-in lists already in agrisage-dev are switched off
// inside the transaction first. The row lock runs for real inside the test transaction; two simultaneous payments
// cannot be reproduced here because the test data is never committed.
[Collection(RealDb.WalkInPriceListCollection)]
public class PaymentsDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private sealed class Env : IAsyncDisposable
    {
        public Env(AgriSageDbContext context, RealDb.MutableUser user)
        {
            Context = context;
            User = user;
            var errors = new NpgsqlErrorClassifier();
            var clock = new DateTimeProvider();
            var audit = new AuditTrail(context, user, clock);
            Categories = new CategoryService(context, errors);
            Products = new ProductService(context, errors);
            StoreProducts = new StoreProductService(context, errors);
            PriceLists = new PriceListService(context, clock, errors, audit);
            Orders = new OrderService(
                context, new OrderBuilder(context, new PriceResolver(context), user, clock, audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(context, clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy()))), new OrderQueries(context),
                user, clock, errors, audit);
            Ledger = new OrderPrepaymentLedger(context);
            Allocator = new PaymentAllocator(clock, new StubCreditReservationAdjuster(), new StubDebtRepaymentPosting());
            Queries = new PaymentQueries(context);
            Payments = new PaymentService(context, new RowLockService(context), Ledger, Allocator, Queries, user, clock, errors, audit);
            Mine = new MyPaymentService(Queries, user);
        }

        public AgriSageDbContext Context { get; }

        public RealDb.MutableUser User { get; }

        public CategoryService Categories { get; }

        public ProductService Products { get; }

        public StoreProductService StoreProducts { get; }

        public PriceListService PriceLists { get; }

        public OrderService Orders { get; }

        public OrderPrepaymentLedger Ledger { get; }

        public PaymentAllocator Allocator { get; }

        public PaymentQueries Queries { get; }

        public PaymentService Payments { get; }

        public MyPaymentService Mine { get; }

        public User Actor { get; set; } = null!;

        public Guid StoreId { get; set; }

        public Guid Bottle { get; set; }

        public Guid StoreProductId { get; set; }

        public Guid BottleId { get; set; }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

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

    // One bottle product priced 100 000 in the walk-in default list, and a signed-in sales actor.
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
        env.Actor = new User(await RoleIdAsync(context, RoleCode.SalesStaff), "Payments Tester", "hash", $"{Tag()}@example.test", null);
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

        var tag = Tag();
        var category = await env.Categories.CreateAsync(new CreateCategoryRequest($"C-{tag}", $"Category {tag}", null, 0, null), Token);
        ProductResponse product = await env.Products.CreateAsync(
            new CreateProductRequest($"SKU-{tag}", $"Product {tag}", category.Id, [new PackagingRequest(env.Bottle, 1, true, false, true, "Bottle")]),
            Token);
        var storeProduct = await env.StoreProducts.CreateAsync(new CreateStoreProductRequest(product.Id), Token);
        env.StoreProductId = storeProduct.Id;
        env.BottleId = product.Packagings.Single().Id;
        await env.PriceLists.UpsertItemsAsync(
            walkIn.Id, new UpsertPriceListItemsRequest([new PriceListItemInput(env.StoreProductId, env.BottleId, 100_000m)]), Token);

        return env;
    }

    private sealed record Farmer(Guid ProfileId, Guid UserId, string Name);

    private static async Task<Farmer> NewFarmerAsync(Env env)
    {
        var name = $"Nông dân {Tag()}";
        var user = new User(await RoleIdAsync(env.Context, RoleCode.Farmer), name, "hash", $"{Tag()}@example.test", "09" + Random.Shared.Next(10_000_000, 99_999_999));
        var farmer = new FarmerProfile(user.Id);
        env.Context.AddRange(user, farmer);
        await env.Context.SaveChangesAsync(Token);

        return new Farmer(farmer.Id, user.Id, name);
    }

    // An order of `quantity` bottles (100 000 each): walk-in, or REGISTERED for the given farmer.
    private static Task<OrderResponse> NewOrderAsync(Env env, long quantity = 3, Farmer? farmer = null, string settlement = "FULL_PAYMENT") =>
        env.Orders.CreateAsync(
            new CreateCounterOrderRequest(
                farmer is null ? "WALK_IN" : "REGISTERED", settlement, "PICKUP",
                [new OrderItemRequest(env.StoreProductId, env.BottleId, quantity)], farmer?.ProfileId),
            Token);

    private static CashPaymentRequest Cash(Guid orderId, decimal amount, string? note = null) =>
        new("ORDER_PAYMENT", amount, orderId, Note: note);

    // ----- Cash payments on orders -----

    [RealDbFact]
    public async Task A_cash_payment_is_paid_and_allocated_to_its_order_with_a_number_and_an_audit_row()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var farmer = await NewFarmerAsync(env);
        var order = await NewOrderAsync(env, 3, farmer);

        var payment = await env.Payments.ReceiveCashAsync(Cash(order.Id, 120_000m, " Đặt cọc "), Token);

        var day = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        Assert.True(DocumentNumbers.SequenceOf(payment.PaymentNumber, DocumentNumbers.Payment, day) > 0);
        Assert.Equal(("ORDER_PAYMENT", "CASH", "PAID", "STAFF", env.Actor.Id, "Đặt cọc"),
            (payment.PaymentContext, payment.PaymentMethod, payment.Status, payment.ConfirmationSource, payment.ConfirmedBy, payment.Note));
        Assert.Equal((120_000m, "VND", 0m, farmer.ProfileId, farmer.Name), (payment.Amount, payment.Currency, payment.UnallocatedAmount, payment.PayerFarmerProfileId, payment.PayerName));
        Assert.NotNull(payment.ConfirmedAt);
        Assert.Null(payment.CheckoutUrl);
        var allocation = Assert.Single(payment.Allocations);
        Assert.Equal(("ORDER", order.Id, order.OrderNumber, 120_000m, 0m, "ACTIVE"),
            (allocation.AllocationType, allocation.OrderId, allocation.OrderNumber, allocation.AllocatedAmount, allocation.PrepaymentConsumedAmount, allocation.Status));
        Assert.Null(allocation.DebtEntryId);

        var summary = await env.Payments.GetOrderSummaryAsync(order.Id, Token);
        Assert.Equal((300_000m, 120_000m, 120_000m, 0m, 180_000m), (summary.OrderTotal, summary.PaidAmount, summary.AvailablePrepayment, summary.ConsumedPrepayment, summary.RemainingToPay));
        Assert.Equal(payment.Id, Assert.Single(summary.Payments).Id);
        Assert.Empty(summary.Refunds);

        var audit = await env.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == payment.Id, Token);
        Assert.Equal(("PAYMENT_RECEIVED", env.Actor.Id), (audit.Action, audit.ActorUserId));

        // A walk-in payer is the order's customer.
        var walkIn = await NewOrderAsync(env, 1);
        var walkInPayment = await env.Payments.ReceiveCashAsync(Cash(walkIn.Id, 100_000m), Token);
        Assert.Equal(("Khách lẻ", null), (walkInPayment.PayerName, walkInPayment.PayerFarmerProfileId));
        Assert.Equal(DocumentNumbers.SequenceOf(payment.PaymentNumber, DocumentNumbers.Payment, day) + 1,
            DocumentNumbers.SequenceOf(walkInPayment.PaymentNumber, DocumentNumbers.Payment, day));
    }

    [RealDbFact]
    public async Task A_payment_cannot_exceed_what_is_left_to_pay_and_cancelled_orders_take_none()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var order = await NewOrderAsync(env, 2);

        await env.Payments.ReceiveCashAsync(Cash(order.Id, 150_000m), Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Payments.ReceiveCashAsync(Cash(order.Id, 50_000.01m), Token));
        await env.Payments.ReceiveCashAsync(Cash(order.Id, 50_000m), Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Payments.ReceiveCashAsync(Cash(order.Id, 1m), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Payments.ReceiveCashAsync(Cash(Guid.NewGuid(), 1m), Token));

        var summary = await env.Payments.GetOrderSummaryAsync(order.Id, Token);
        Assert.Equal((200_000m, 0m, 2), (summary.PaidAmount, summary.RemainingToPay, summary.Payments.Count));

        var cancelled = await NewOrderAsync(env, 1);
        var entity = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == cancelled.Id, Token);
        entity.Cancel(env.Actor.Id, Now, "Khách đổi ý");
        await env.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Payments.ReceiveCashAsync(Cash(cancelled.Id, 1m), Token));
        Assert.Equal(2, await env.Context.Payments.CountAsync(p => p.OrderId == order.Id, Token));
        Assert.Equal(0, await env.Context.Payments.CountAsync(p => p.OrderId == cancelled.Id, Token));
    }

    [RealDbFact]
    public async Task A_payment_on_a_confirmed_credit_order_is_allocated_without_the_credit_adjuster_failing()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var farmer = await NewFarmerAsync(env);
        var order = await NewOrderAsync(env, 2, farmer, "CREDIT");
        var entity = await env.Context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id, Token);
        entity.Confirm(env.Actor.Id, Now, creditTermDays: 30);
        await env.Context.SaveChangesAsync(Token);

        var payment = await env.Payments.ReceiveCashAsync(Cash(order.Id, 50_000m), Token);

        Assert.Equal(50_000m, Assert.Single(payment.Allocations).AllocatedAmount);
        Assert.Equal(50_000m, await env.Ledger.GetPaidAmountAsync(order.Id, Token));
    }

    [RealDbFact]
    public async Task Debt_repayments_check_the_customer_and_the_balance_and_are_refused_until_debts_exist()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var farmer = await NewFarmerAsync(env);
        CashPaymentRequest Repay(Guid? farmerId, decimal amount) => new("DEBT_REPAYMENT", amount, FarmerProfileId: farmerId);

        await Assert.ThrowsAsync<NotFoundException>(() => env.Payments.ReceiveCashAsync(Repay(Guid.NewGuid(), 10m), Token));
        // No debt account → nothing owed.
        var owes = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Payments.ReceiveCashAsync(Repay(farmer.ProfileId, 10m), Token));
        Assert.Contains("owes", owes.Message);
        Assert.Equal(0, await env.Context.Payments.CountAsync(p => p.PayerFarmerProfileId == farmer.ProfileId, Token));
    }

    // ----- Ledger and allocator -----

    [RealDbFact]
    public async Task The_ledger_counts_paid_and_available_money_and_consumes_the_oldest_allocation_first()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var order = await NewOrderAsync(env, 3);

        Assert.Equal((0m, 0m, 0m), (
            await env.Ledger.GetPaidAmountAsync(order.Id, Token), await env.Ledger.GetAvailableAsync(order.Id, Token),
            await env.Ledger.ConsumeAsync(order.Id, 10m, Token)));

        var first = await env.Payments.ReceiveCashAsync(Cash(order.Id, 100_000m), Token);
        var second = await env.Payments.ReceiveCashAsync(Cash(order.Id, 50_000m), Token);
        Assert.Equal((150_000m, 150_000m), (await env.Ledger.GetPaidAmountAsync(order.Id, Token), await env.Ledger.GetAvailableAsync(order.Id, Token)));

        Assert.Equal(120_000m, await env.Ledger.ConsumeAsync(order.Id, 120_000m, Token));
        await env.Context.SaveChangesAsync(Token);

        Assert.Equal((150_000m, 30_000m), (await env.Ledger.GetPaidAmountAsync(order.Id, Token), await env.Ledger.GetAvailableAsync(order.Id, Token)));
        Assert.Equal(100_000m, (await env.Payments.GetAsync(first.Id, Token)).Allocations.Single().PrepaymentConsumedAmount);
        Assert.Equal(20_000m, (await env.Payments.GetAsync(second.Id, Token)).Allocations.Single().PrepaymentConsumedAmount);
        var summary = await env.Payments.GetOrderSummaryAsync(order.Id, Token);
        Assert.Equal((150_000m, 30_000m, 120_000m, 150_000m), (summary.PaidAmount, summary.AvailablePrepayment, summary.ConsumedPrepayment, summary.RemainingToPay));

        // More than is left consumes only what is left.
        Assert.Equal(30_000m, await env.Ledger.ConsumeAsync(order.Id, 1_000_000m, Token));
        Assert.Equal(0m, await env.Ledger.GetAvailableAsync(order.Id, Token));
    }

    [RealDbFact]
    public async Task The_ledger_also_sees_payments_not_yet_saved_in_the_same_unit_of_work()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var order = await NewOrderAsync(env, 2);
        await env.Payments.ReceiveCashAsync(Cash(order.Id, 60_000m), Token);

        var storeId = env.StoreId;
        var unsaved = new Payment(storeId, "PM-UNSAVED-1", PaymentContext.OrderPayment, PaymentMethod.Cash, 40_000m, Now, null, env.Actor.Id, orderId: order.Id);
        unsaved.MarkPaid(PaymentConfirmationSource.Staff, Now, env.Actor.Id);
        env.Context.Payments.Add(unsaved);
        var entity = await env.Context.Orders.SingleAsync(o => o.Id == order.Id, Token);
        await env.Allocator.AllocateAsync(unsaved, entity, null, env.Actor.Id, Token);

        Assert.Equal((100_000m, 100_000m), (await env.Ledger.GetPaidAmountAsync(order.Id, Token), await env.Ledger.GetAvailableAsync(order.Id, Token)));
        Assert.Equal(70_000m, await env.Ledger.ConsumeAsync(order.Id, 70_000m, Token));
        Assert.Equal(30_000m, await env.Ledger.GetAvailableAsync(order.Id, Token));
        Assert.Equal(40_000m, unsaved.Allocations.Single().AllocatedAmount);
    }

    [RealDbFact]
    public async Task The_allocator_refuses_an_unpaid_payment_and_a_payment_for_another_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var order = await NewOrderAsync(env, 1);
        var other = await NewOrderAsync(env, 1);
        var entity = await env.Context.Orders.SingleAsync(o => o.Id == order.Id, Token);
        var otherEntity = await env.Context.Orders.SingleAsync(o => o.Id == other.Id, Token);
        var pending = new Payment(env.StoreId, "PM-PENDING-1", PaymentContext.OrderPayment, PaymentMethod.Cash, 10m, Now, null, env.Actor.Id, orderId: order.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Allocator.AllocateAsync(pending, entity, null, env.Actor.Id, Token));
        pending.MarkPaid(PaymentConfirmationSource.Staff, Now, env.Actor.Id);
        await Assert.ThrowsAsync<DomainException>(() => env.Allocator.AllocateAsync(pending, otherEntity, null, env.Actor.Id, Token));
        await env.Allocator.AllocateAsync(pending, entity, null, env.Actor.Id, Token);
        Assert.Equal(0m, pending.UnallocatedAmount);
    }

    // ----- Queries and cancelling -----

    [RealDbFact]
    public async Task Payments_are_listed_newest_first_with_filters_and_a_pending_cash_payment_can_be_cancelled()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var farmer = await NewFarmerAsync(env);
        var order = await NewOrderAsync(env, 2, farmer);
        var other = await NewOrderAsync(env, 1);
        var first = await env.Payments.ReceiveCashAsync(Cash(order.Id, 50_000m), Token);
        var second = await env.Payments.ReceiveCashAsync(Cash(other.Id, 100_000m), Token);

        var byOrder = await env.Payments.ListAsync(new PaymentListRequest { OrderId = order.Id }, Token);
        var item = Assert.Single(byOrder.Items);
        Assert.Equal((first.Id, "ORDER_PAYMENT", "CASH", "PAID", farmer.Name), (item.Id, item.PaymentContext, item.PaymentMethod, item.Status, item.PayerName));
        Assert.Equal(second.Id, Assert.Single((await env.Payments.ListAsync(new PaymentListRequest { Search = second.PaymentNumber.ToLowerInvariant() }, Token)).Items).Id);
        Assert.Equal(first.Id, Assert.Single((await env.Payments.ListAsync(new PaymentListRequest { FarmerProfileId = farmer.ProfileId }, Token)).Items).Id);
        Assert.Empty((await env.Payments.ListAsync(new PaymentListRequest { PaymentMethod = "payos", OrderId = order.Id }, Token)).Items);
        Assert.Empty((await env.Payments.ListAsync(new PaymentListRequest { Status = "PENDING", OrderId = order.Id }, Token)).Items);
        Assert.Empty((await env.Payments.ListAsync(new PaymentListRequest { PaymentContext = "DEBT_REPAYMENT", OrderId = order.Id }, Token)).Items);
        var today = BusinessCalendar.Today(Now);
        var page = await env.Payments.ListAsync(new PaymentListRequest { FromDate = today, ToDate = today, PageSize = 1 }, Token);
        Assert.Equal((second.Id, true), (Assert.Single(page.Items).Id, page.TotalCount >= 2));
        Assert.Empty((await env.Payments.ListAsync(new PaymentListRequest { FromDate = today.AddDays(1) }, Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Payments.GetAsync(Guid.NewGuid(), Token));

        // A cash payment is PAID at once; only a PENDING one can be cancelled.
        await Assert.ThrowsAsync<DomainException>(() => env.Payments.CancelAsync(first.Id, new CancelPaymentRequest("nhầm"), Token));
        var pending = new Payment(env.StoreId, "PM-PENDING-2", PaymentContext.OrderPayment, PaymentMethod.Cash, 10m, Now, null, env.Actor.Id, orderId: other.Id);
        var online = new Payment(env.StoreId, "PM-PENDING-3", PaymentContext.OrderPayment, PaymentMethod.PayOs, 10m, Now, null, env.Actor.Id, orderId: other.Id);
        env.Context.Payments.AddRange(pending, online);
        await env.Context.SaveChangesAsync(Token);

        var cancelled = await env.Payments.CancelAsync(pending.Id, new CancelPaymentRequest(" Khách không trả "), Token);
        Assert.Equal(("CANCELLED", true), (cancelled.Status, cancelled.CancelledAt is not null));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Payments.CancelAsync(online.Id, new CancelPaymentRequest(), Token));
        Assert.Equal("Khách không trả", (await env.Context.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == pending.Id && a.Action == "PAYMENT_CANCELLED", Token)).Reason);
        Assert.Equal(100_000m, (await env.Payments.GetOrderSummaryAsync(other.Id, Token)).PaidAmount);
    }

    [RealDbFact]
    public async Task A_farmer_sees_only_their_own_payments_and_orders()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var mine = await NewFarmerAsync(env);
        var theirs = await NewFarmerAsync(env);
        var myOrder = await NewOrderAsync(env, 1, mine);
        var theirOrder = await NewOrderAsync(env, 1, theirs);
        var myPayment = await env.Payments.ReceiveCashAsync(Cash(myOrder.Id, 100_000m), Token);
        var theirPayment = await env.Payments.ReceiveCashAsync(Cash(theirOrder.Id, 100_000m), Token);

        env.User.UserId = mine.UserId;
        env.User.Role = "FARMER";

        Assert.Equal(myPayment.Id, Assert.Single((await env.Mine.ListAsync(new MyPaymentListRequest(), Token)).Items).Id);
        Assert.Empty((await env.Mine.ListAsync(new MyPaymentListRequest { Status = "CANCELLED" }, Token)).Items);
        Assert.Equal(myPayment.Id, (await env.Mine.GetAsync(myPayment.Id, Token)).Id);
        Assert.Equal(100_000m, (await env.Mine.GetOrderSummaryAsync(myOrder.Id, Token)).PaidAmount);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Mine.GetAsync(theirPayment.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Mine.GetOrderSummaryAsync(theirOrder.Id, Token));

        // A user without a customer profile has nothing to see.
        env.User.UserId = env.Actor.Id;
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Mine.ListAsync(new MyPaymentListRequest(), Token));
    }
}
