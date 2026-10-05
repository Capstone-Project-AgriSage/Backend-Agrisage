using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Tests.TestDoubles;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// FLOW_2 §5 (task F2.3) against PostgreSQL; every session rolls back.
[Collection(RealDb.WalkInPriceListCollection)]
public class MeOrdersDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Env(OnlineOrderTestData data)
    {
        private readonly NpgsqlErrorClassifier _errors = new();
        private readonly DateTimeProvider _clock = new();

        public OnlineOrderTestData Data { get; } = data;

        private AuditTrail Audit => new(Data.Context, Data.User, _clock);

        private RowLockService Locks => new(Data.Context);

        public CartService Carts => new(Data.Context, new CurrentFarmer(Data.Context, Data.User), new PriceResolver(Data.Context),
            Locks, _errors, Data.User, _clock);

        public MyProfileService Profile => new(Data.Context, Data.User, _errors, Locks,
            new UserAddressDefaultSwitcher(Data.Context), new CustomerAddresses(Data.Context));

        public MeOrderService Orders
        {
            get
            {
                var builder = new OrderBuilder(Data.Context, new PriceResolver(Data.Context), Data.User, _clock, Audit, new AgriSage.Application.Features.Credit.CreditEligibilityService(Data.Context, _clock, Microsoft.Extensions.Options.Options.Create(new AgriSage.Application.Features.Credit.CreditPolicy())));
                var queries = new OrderQueries(Data.Context);
                return new MeOrderService(Data.Context, new CurrentFarmer(Data.Context, Data.User), builder, queries,
                    new OrderService(Data.Context, builder, queries, Data.User, _clock, _errors, Audit),
                    new OrderCanceller(Data.Context, Locks, new StubOrderSettlementGuard(),
                        new OrderPaymentCancellation(Data.Context, Locks, new FakePaymentGateway(), _clock, Audit)),
                    Locks, _clock, _errors, Audit);
            }
        }

        public PaymentService Payments => new(Data.Context, Locks, new OrderPrepaymentLedger(Data.Context),
            new PaymentAllocator(_clock, new StubCreditReservationAdjuster(), new StubDebtRepaymentPosting()),
            new PaymentQueries(Data.Context), Data.User, _clock, _errors, Audit);
    }

    private static async Task<(Env Env, OnlineOrderTestData.Priced Product, OnlineOrderTestData.Farmer Farmer)> PrepareAsync(
        RealDb.Session session)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var farmer = await data.ActAsNewFarmerAsync();
        return (new Env(data), product, farmer);
    }

    private static readonly CheckoutRequest Pickup = new("FARMER_WEB", "FULL_PAYMENT", "PICKUP");

    [RealDbFact]
    public async Task Checkout_builds_the_order_from_the_cart_and_converts_the_cart_in_one_transaction()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, product, farmer) = await PrepareAsync(session);
        var cart = await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 2), Token);
        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BottleId, 3), Token);

        var order = await env.Orders.CheckoutAsync(Pickup with { Note = "  Giao buổi sáng " }, Token);

        Assert.Equal("PENDING_CONFIRMATION", order.Status);
        Assert.Equal("FARMER_WEB", order.Source);
        Assert.Equal("REGISTERED", order.CustomerType);
        Assert.Equal(farmer.ProfileId, order.FarmerProfileId);
        Assert.Equal(farmer.Name, order.CustomerName);
        Assert.Equal(farmer.Phone, order.CustomerPhone);
        Assert.Equal(env.Data.WalkInListId, order.PriceListId);
        Assert.Equal(farmer.UserId, order.CreatedBy);
        Assert.Equal("Giao buổi sáng", order.Note);
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(2 * 55_000m + 3 * 10_000m, order.TotalAmount);

        var converted = await env.Data.Context.Carts.AsNoTracking().SingleAsync(c => c.Id == cart.Id, Token);
        Assert.Equal(CartStatus.Converted, converted.Status);
        Assert.Equal(order.Id, converted.ConvertedOrderId);
        Assert.Null((await env.Carts.GetAsync(Token)).Id); // the next item starts a new cart
    }

    [RealDbFact]
    public async Task Delivery_copies_an_own_saved_address_or_takes_a_typed_one()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, product, _) = await PrepareAsync(session);
        var address = await env.Profile.CreateAddressAsync(
            new AddressRequest("Nguyen Van A", "0901234567", "Ap 3", "Can Tho", "FARM", "Tan Phu", "Cai Rang"), Token);

        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        var saved = await env.Orders.CheckoutAsync(new("FARMER_MOBILE", "FULL_PAYMENT", "DELIVERY", AddressId: address.Id), Token);
        Assert.Equal("FARMER_MOBILE", saved.Source);
        Assert.Equal("Ap 3", saved.DeliveryAddress!.AddressLine);
        Assert.Equal("Cai Rang", saved.DeliveryAddress.District);

        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        var typed = await env.Orders.CheckoutAsync(
            new("FARMER_WEB", "FULL_PAYMENT", "DELIVERY", DeliveryAddress: new("Tran B", "+84 912 345 678", "Ap 5", "Hau Giang")), Token);
        Assert.Equal("0912345678", typed.DeliveryAddress!.RecipientPhone);

        // Someone else's saved address is refused and nothing is saved: the cart stays ACTIVE.
        await ActAsAnotherFarmerWithACartAsync(env, product);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            env.Orders.CheckoutAsync(new("FARMER_WEB", "FULL_PAYMENT", "DELIVERY", AddressId: address.Id), Token));
        Assert.NotNull((await env.Carts.GetAsync(Token)).Id);
    }

    private static async Task ActAsAnotherFarmerWithACartAsync(Env env, OnlineOrderTestData.Priced product)
    {
        await env.Data.ActAsNewFarmerAsync();
        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
    }

    [RealDbFact]
    public async Task An_empty_cart_or_a_line_that_can_no_longer_be_ordered_fails_and_saves_nothing()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, product, farmer) = await PrepareAsync(session);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CheckoutAsync(Pickup, Token));

        var cart = await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        env.Data.ActAsStaff();
        await env.Data.StoreProducts.SetSellableAsync(product.StoreProductId, false, Token);
        env.Data.ActAs(farmer.UserId, "FARMER");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CheckoutAsync(Pickup, Token));
        Assert.Contains("items[0]", error.Errors!.Keys);
        Assert.Equal(CartStatus.Active, (await env.Data.Context.Carts.AsNoTracking().SingleAsync(c => c.Id == cart.Id, Token)).Status);
        Assert.False(await env.Data.Context.Orders.AsNoTracking().AnyAsync(o => o.FarmerProfileId == farmer.ProfileId, Token));
    }

    [RealDbFact]
    public async Task A_farmer_lists_and_reads_only_their_own_orders()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, product, _) = await PrepareAsync(session);
        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 1), Token);
        var mine = await env.Orders.CheckoutAsync(Pickup, Token);

        var page = await env.Orders.ListAsync(new MyOrderListRequest(), Token);
        Assert.Equal(mine.Id, Assert.Single(page.Items).Id);
        Assert.Empty((await env.Orders.ListAsync(new MyOrderListRequest { Status = "CANCELLED" }, Token)).Items);
        Assert.Equal(mine.OrderNumber, (await env.Orders.GetAsync(mine.Id, Token)).OrderNumber);

        await env.Data.ActAsNewFarmerAsync();
        Assert.Empty((await env.Orders.ListAsync(new MyOrderListRequest(), Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.GetAsync(mine.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Orders.CancelAsync(mine.Id, new(), Token));
    }

    [RealDbFact]
    public async Task Cancel_only_while_pending_and_a_paid_payment_gets_a_refund_request()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, product, farmer) = await PrepareAsync(session);
        await env.Carts.AddItemAsync(new(product.StoreProductId, product.BoxId, 2), Token);
        var order = await env.Orders.CheckoutAsync(Pickup, Token);

        // The store takes a cash prepayment for the online order.
        env.Data.ActAsStaff();
        await env.Payments.ReceiveCashAsync(new CashPaymentRequest("ORDER_PAYMENT", 110_000m, order.Id), Token);
        env.Data.ActAs(farmer.UserId, "FARMER");

        var cancelled = await env.Orders.CancelAsync(order.Id, new(), Token);
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal(MeOrderService.DefaultCancelReason, cancelled.CancelReason);
        Assert.Equal(farmer.UserId, cancelled.CancelledBy);
        var refund = await env.Data.Context.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == order.Id, Token);
        Assert.Equal(110_000m, refund.Amount);

        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Orders.CancelAsync(order.Id, new("again"), Token));
    }
}
