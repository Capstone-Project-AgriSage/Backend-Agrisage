using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Domain.Features.Orders;

public class OrderTests
{
    private static Order CreateWalkInOrder(SettlementType settlementType, Guid? farmerProfileId = null) =>
        new(
            Guid.NewGuid(),
            "SO-0002",
            OrderSource.Counter,
            CustomerType.WalkIn,
            StaffId,
            "Walk-in customer",
            settlementType,
            FulfillmentType.Pickup,
            farmerProfileId);

    [Fact]
    public void Walk_in_order_cannot_use_credit_or_a_farmer_profile()
    {
        Assert.Throws<DomainException>(() => CreateWalkInOrder(SettlementType.Credit));
        Assert.Throws<DomainException>(() => CreateWalkInOrder(SettlementType.FullPayment, Guid.NewGuid()));

        Assert.Equal(CustomerType.WalkIn, CreateWalkInOrder(SettlementType.FullPayment).CustomerType);
    }

    [Fact]
    public void Registered_order_requires_a_farmer_profile()
    {
        Assert.Throws<DomainException>(() => new Order(
            Guid.NewGuid(), "SO-0003", OrderSource.FarmerWeb, CustomerType.Registered, StaffId,
            "Nguyen Van A", SettlementType.Credit, FulfillmentType.Pickup));
    }

    [Fact]
    public void Delivery_order_requires_a_complete_delivery_address()
    {
        Assert.Throws<DomainException>(() => new Order(
            Guid.NewGuid(), "SO-0004", OrderSource.FarmerMobile, CustomerType.Registered, StaffId,
            "Nguyen Van A", SettlementType.FullPayment, FulfillmentType.Delivery, Guid.NewGuid()));

        Assert.Throws<DomainException>(() => new Order(
            Guid.NewGuid(), "SO-0005", OrderSource.FarmerMobile, CustomerType.Registered, StaffId,
            "Nguyen Van A", SettlementType.FullPayment, FulfillmentType.Delivery, Guid.NewGuid(),
            deliveryAddress: Address with { Province = " " }));

        var order = CreateOrder(FulfillmentType.Delivery);
        Assert.Equal("Can Tho", order.DeliveryProvince);
    }

    [Fact]
    public void Item_snapshots_conversion_and_price_and_updates_totals()
    {
        var (order, item) = CreateOrderWithItem();

        Assert.Equal(6, item.ConversionToBaseSnapshot);
        Assert.Equal(BaseQuantity, item.BaseQuantity);
        Assert.Equal(BoxPrice, item.SuggestedUnitPrice);
        Assert.Equal(BoxPrice, item.UnitPrice);
        Assert.False(item.PriceOverridden);
        Assert.Equal(240_000m, item.LineTotalAmount);
        Assert.Equal(240_000m, order.SubtotalAmount);
        Assert.Equal(240_000m, order.TotalAmount);
    }

    [Fact]
    public void Price_override_keeps_suggested_price_actor_and_reason()
    {
        var (order, item) = CreateOrderWithItem();

        Assert.Throws<DomainException>(() =>
            order.OverrideItemPrice(item.Id, new PriceOverride(110_000m, StaffId, " ")));
        Assert.Throws<DomainException>(() =>
            order.OverrideItemPrice(item.Id, new PriceOverride(110_000m, Guid.Empty, "Loyal customer")));
        Assert.Throws<DomainException>(() =>
            order.OverrideItemPrice(item.Id, new PriceOverride(110_000.555m, StaffId, "Loyal customer")));

        order.OverrideItemPrice(item.Id, new PriceOverride(110_000m, StaffId, "Loyal customer"));

        Assert.True(item.PriceOverridden);
        Assert.Equal(BoxPrice, item.SuggestedUnitPrice);
        Assert.Equal(110_000m, item.UnitPrice);
        Assert.Equal(StaffId, item.OverriddenBy);
        Assert.Equal("Loyal customer", item.OverrideReason);
        Assert.Equal(220_000m, order.TotalAmount);
    }

    [Fact]
    public void Credit_order_confirmation_requires_a_credit_term_and_full_payment_rejects_one()
    {
        var (credit, _) = CreateOrderWithItem(settlementType: SettlementType.Credit);
        Assert.Throws<DomainException>(() => credit.Confirm(StaffId, Now));
        credit.Confirm(StaffId, Now, creditTermDays: 30);
        Assert.Equal(30, credit.CreditTermDaysSnapshot);

        var (fullPayment, _) = CreateOrderWithItem();
        Assert.Throws<DomainException>(() => fullPayment.Confirm(StaffId, Now, creditTermDays: 30));
    }

    [Fact]
    public void Order_without_items_cannot_be_confirmed()
    {
        var order = CreateOrder();

        Assert.Throws<DomainException>(() => order.Confirm(StaffId, Now));
    }

    [Fact]
    public void Items_cannot_change_after_confirmation()
    {
        var (order, item) = CreateConfirmedOrderWithItem();

        Assert.Throws<DomainException>(() => order.UpdateItemQuantity(item.Id, 3));
        Assert.Throws<DomainException>(() => order.OverrideItemPrice(item.Id, null));
        Assert.Throws<DomainException>(() => order.RemoveItem(item.Id, StaffId, Now));
        Assert.Throws<DomainException>(() => order.MarkDeleted(StaffId, Now));
    }

    [Fact]
    public void Pickup_fulfillment_moves_to_partially_fulfilled_then_completed()
    {
        var (order, item) = CreateConfirmedOrderWithItem();
        order.MarkReadyForFulfillment();

        order.RecordFulfillment(item.Id, 6, StaffId, Now);
        Assert.Equal(OrderStatus.PartiallyFulfilled, order.Status);
        Assert.Equal(OrderItemStatus.PartiallyFulfilled, item.Status);
        Assert.Null(order.PickupCompletedAt);

        order.RecordFulfillment(item.Id, 6, StaffId, Now.AddHours(1));
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(OrderItemStatus.Fulfilled, item.Status);
        Assert.Equal(Now.AddHours(1), order.CompletedAt);
        Assert.Equal(StaffId, order.PickupCompletedBy);
        Assert.Equal(Now.AddHours(1), order.PickupCompletedAt);
    }

    [Fact]
    public void Fulfilled_and_cancelled_line_ends_partially_cancelled_and_stays_closed()
    {
        var (order, item) = CreateConfirmedOrderWithItem();
        order.RecordFulfillment(item.Id, 6, StaffId, Now);

        order.CancelItemRemaining(item.Id, StaffId, Now);

        Assert.Equal(OrderStatus.PartiallyCancelled, order.Status);
        Assert.Equal(OrderItemStatus.PartiallyCancelled, item.Status);
        Assert.Equal(6, item.CancelledBaseQuantity);
        Assert.Throws<DomainException>(() => order.RecordFulfillment(item.Id, 1, StaffId, Now));
        Assert.Throws<DomainException>(() => order.Cancel(StaffId, Now));
    }

    [Fact]
    public void Cancelling_the_rest_keeps_the_reason_only_when_it_ends_the_order()
    {
        var (open, openItem) = CreateConfirmedOrderWithItem();
        open.RecordFulfillment(openItem.Id, 2, StaffId, Now);
        Assert.Equal(OrderStatus.PartiallyFulfilled, open.Status);
        Assert.Null(open.CancelReason);

        var (ended, item) = CreateConfirmedOrderWithItem();
        ended.RecordFulfillment(item.Id, 6, StaffId, Now);
        ended.CancelItemRemaining(item.Id, StaffId, Now, "Khách không lấy nữa");
        Assert.Equal(("Khách không lấy nữa", OrderStatus.PartiallyCancelled), (ended.CancelReason, ended.Status));

        var (untouched, untouchedItem) = CreateConfirmedOrderWithItem();
        untouched.CancelItemRemaining(untouchedItem.Id, StaffId, Now, "Đặt nhầm");
        Assert.Equal(("Đặt nhầm", OrderStatus.Cancelled), (untouched.CancelReason, untouched.Status));
    }

    [Fact]
    public void Full_cancellation_is_only_possible_before_fulfillment()
    {
        var (cancellable, cancellableItem) = CreateConfirmedOrderWithItem();
        cancellable.Cancel(StaffId, Now, "Customer changed mind");
        Assert.Equal(OrderStatus.Cancelled, cancellable.Status);
        Assert.Equal(OrderItemStatus.Cancelled, cancellableItem.Status);
        Assert.Equal("Customer changed mind", cancellable.CancelReason);

        var (fulfilled, item) = CreateConfirmedOrderWithItem();
        fulfilled.RecordFulfillment(item.Id, 1, StaffId, Now);
        Assert.Throws<DomainException>(() => fulfilled.Cancel(StaffId, Now));
    }

    [Fact]
    public void Fulfillment_cannot_exceed_remaining_quantity()
    {
        var (order, item) = CreateConfirmedOrderWithItem();

        Assert.Throws<DomainException>(() => order.RecordFulfillment(item.Id, BaseQuantity + 1, StaffId, Now));
        Assert.Equal(0, item.FulfilledBaseQuantity);
    }

    [Fact]
    public void Pending_order_cannot_record_fulfillment()
    {
        var (order, item) = CreateOrderWithItem();

        Assert.Throws<DomainException>(() => order.RecordFulfillment(item.Id, 1, StaffId, Now));
    }

    [Fact]
    public void Order_item_cannot_be_deleted_directly()
    {
        var (_, item) = CreateOrderWithItem();

        Assert.Throws<DomainException>(() => item.MarkDeleted(StaffId, Now));
    }
}
