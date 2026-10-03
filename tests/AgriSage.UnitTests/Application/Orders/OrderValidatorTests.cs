using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Common.Exceptions;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Application.Orders;

// Request validation of the counter-order API (FLOW_1 §4), before any database access.
public class OrderValidatorTests
{
    private static OrderItemRequest Line(long quantity = 1, decimal? price = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), quantity, price);

    private static DeliveryAddressRequest Address() => new("Chú Tư", "0912345678", "Ấp 3", "Cần Thơ");

    private static CreateCounterOrderRequest Walk(
        string fulfillment = "PICKUP", DeliveryAddressRequest? address = null, Guid? addressId = null, params OrderItemRequest[] items) =>
        new("WALK_IN", "FULL_PAYMENT", fulfillment, items.Length == 0 ? [Line()] : items, null, null, null, addressId, address);

    private static List<string> Errors(CreateCounterOrderRequest request) =>
        new CreateCounterOrderRequestValidator().Validate(request).Errors.Select(e => e.PropertyName).ToList();

    [Fact]
    public void A_walk_in_name_is_optional_and_other_texts_are_bounded()
    {
        var validator = new CreateCounterOrderRequestValidator();

        Assert.True(validator.Validate(Walk()).IsValid);
        Assert.True(validator.Validate(Walk() with { CustomerName = "   " }).IsValid);
        Assert.False(validator.Validate(Walk() with { CustomerName = new string('N', 151) }).IsValid);
        Assert.False(validator.Validate(Walk() with { CustomerPhone = new string('0', 21) }).IsValid);
        Assert.False(validator.Validate(Walk() with { Note = new string('x', 1001) }).IsValid);
    }

    [Fact]
    public void Enum_texts_must_be_known()
    {
        Assert.Contains("CustomerType", Errors(Walk() with { CustomerType = "VIP" }));
        Assert.Contains("SettlementType", Errors(Walk() with { SettlementType = "LATER" }));
        Assert.Contains("FulfillmentType", Errors(Walk() with { FulfillmentType = "COURIER" }));
        Assert.Empty(Errors(Walk() with { CustomerType = "walk_in", SettlementType = "full_payment", FulfillmentType = "pickup" }));
    }

    [Fact]
    public void A_registered_order_needs_a_farmer()
    {
        var registered = Walk() with { CustomerType = "REGISTERED" };

        Assert.Contains("FarmerProfileId", Errors(registered));
        Assert.Empty(Errors(registered with { FarmerProfileId = Guid.NewGuid() }));
    }

    [Fact]
    public void Lines_are_not_empty_bounded_unique_per_pair_and_positive()
    {
        var line = Line();
        var tooMany = Enumerable.Range(0, CreateCounterOrderRequestValidator.MaxItems + 1).Select(_ => Line()).ToArray();

        Assert.NotEmpty(Errors(Walk() with { Items = [] }));
        Assert.NotEmpty(Errors(Walk(items: [line, line with { Quantity = 2 }])));
        Assert.NotEmpty(Errors(Walk(items: tooMany)));
        Assert.Empty(Errors(Walk(items: tooMany.Take(CreateCounterOrderRequestValidator.MaxItems).ToArray())));
        Assert.NotEmpty(Errors(Walk(items: Line(0))));
        Assert.NotEmpty(Errors(Walk(items: Line(OrderItemRequestValidator.MaxQuantity + 1))));
        Assert.NotEmpty(Errors(Walk(items: line with { StoreProductId = Guid.Empty })));
        Assert.NotEmpty(Errors(Walk(items: Line(price: 1.234m))));
        Assert.NotEmpty(Errors(Walk(items: Line(price: -1m))));
        Assert.Empty(Errors(Walk(items: [Line(price: 0m), Line(price: 12.5m)])));
        Assert.NotEmpty(Errors(Walk(items: Line() with { OverrideReason = new string('r', 501) })));
    }

    [Fact]
    public void Delivery_needs_exactly_one_address_and_pickup_none()
    {
        Assert.NotEmpty(Errors(Walk("DELIVERY")));
        Assert.NotEmpty(Errors(Walk("DELIVERY", Address(), Guid.NewGuid())));
        Assert.Empty(Errors(Walk("DELIVERY", Address())));
        Assert.Empty(Errors(Walk("DELIVERY", addressId: Guid.NewGuid())));
        Assert.NotEmpty(Errors(Walk("PICKUP", Address())));
        Assert.NotEmpty(Errors(Walk("PICKUP", addressId: Guid.NewGuid())));
    }

    [Fact]
    public void A_typed_address_is_complete()
    {
        var validator = new DeliveryAddressRequestValidator();

        Assert.True(validator.Validate(Address()).IsValid);
        Assert.False(validator.Validate(Address() with { RecipientName = "" }).IsValid);
        Assert.False(validator.Validate(Address() with { RecipientPhone = " " }).IsValid);
        Assert.False(validator.Validate(Address() with { AddressLine = "" }).IsValid);
        Assert.False(validator.Validate(Address() with { Province = "" }).IsValid);
        Assert.False(validator.Validate(Address() with { Latitude = 91m }).IsValid);
        Assert.False(validator.Validate(Address() with { Longitude = -181m }).IsValid);
        Assert.True(validator.Validate(Address() with { Latitude = 10.0312345m, Longitude = 105.7m }).IsValid);
        Assert.NotEmpty(Errors(Walk("DELIVERY", Address() with { Province = "" })));
    }

    [Fact]
    public void Updates_take_one_address_at_most()
    {
        var validator = new UpdateOrderRequestValidator();

        Assert.True(validator.Validate(new UpdateOrderRequest()).IsValid);
        Assert.True(validator.Validate(new UpdateOrderRequest(Note: "ghi chú")).IsValid);
        Assert.True(validator.Validate(new UpdateOrderRequest(AddressId: Guid.NewGuid())).IsValid);
        Assert.False(validator.Validate(new UpdateOrderRequest(Guid.NewGuid(), Address())).IsValid);
        Assert.False(validator.Validate(new UpdateOrderRequest(Note: new string('x', 1001))).IsValid);
    }

    [Fact]
    public void Quantity_and_price_changes_are_validated()
    {
        var quantity = new ChangeOrderItemQuantityRequestValidator();
        var price = new OverrideOrderItemPriceRequestValidator();

        Assert.True(quantity.Validate(new ChangeOrderItemQuantityRequest(1)).IsValid);
        Assert.False(quantity.Validate(new ChangeOrderItemQuantityRequest(0)).IsValid);
        Assert.True(price.Validate(new OverrideOrderItemPriceRequest(0m, "Tặng")).IsValid);
        Assert.False(price.Validate(new OverrideOrderItemPriceRequest(10m, " ")).IsValid);
        Assert.False(price.Validate(new OverrideOrderItemPriceRequest(10.001m, "x")).IsValid);
        Assert.False(price.Validate(new OverrideOrderItemPriceRequest(-1m, "x")).IsValid);
    }

    [Fact]
    public void List_filters_accept_only_known_values_and_ordered_dates()
    {
        var validator = new OrderListRequestValidator();
        var day = new DateOnly(2026, 10, 3);

        Assert.True(validator.Validate(new OrderListRequest
        {
            Status = "pending_confirmation", CustomerType = "WALK_IN", SettlementType = "CREDIT", FulfillmentType = "PICKUP",
            Source = "COUNTER", FromDate = day, ToDate = day, Search = "OD-2026"
        }).IsValid);
        Assert.False(validator.Validate(new OrderListRequest { Status = "SHIPPED" }).IsValid);
        Assert.False(validator.Validate(new OrderListRequest { CustomerType = "VIP" }).IsValid);
        Assert.False(validator.Validate(new OrderListRequest { Source = "PHONE" }).IsValid);
        Assert.False(validator.Validate(new OrderListRequest { FromDate = day, ToDate = day.AddDays(-1) }).IsValid);
        Assert.False(validator.Validate(new OrderListRequest { PageSize = 0 }).IsValid);
    }

    [Fact]
    public void The_note_changes_only_while_the_order_is_pending()
    {
        var order = CreateOrder();

        order.ChangeNote("Giao trước 5 giờ");
        Assert.Equal("Giao trước 5 giờ", order.Note);
        order.ChangeNote(null);
        Assert.Null(order.Note);

        var (confirmed, _) = CreateOrderWithItem();
        confirmed.Confirm(StaffId, Now);
        Assert.Throws<DomainException>(() => confirmed.ChangeNote("muộn"));
    }
}
