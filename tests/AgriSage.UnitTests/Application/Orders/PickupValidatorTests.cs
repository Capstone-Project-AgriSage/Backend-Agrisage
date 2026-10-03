using AgriSage.Application.Features.Orders;

namespace AgriSage.UnitTests.Application.Orders;

// Request validation of pickup and cancel-remaining (FLOW_1 §7), before any database access.
public class PickupValidatorTests
{
    private static PickupLotRequest Lot(long quantity = 10) => new(Guid.NewGuid(), quantity);

    private static PickupItemRequest Item(params PickupLotRequest[] lots) => new(Guid.NewGuid(), lots.Length == 0 ? [Lot()] : lots);

    private static bool Valid(PickupRequest request) => new PickupRequestValidator().Validate(request).IsValid;

    [Fact]
    public void A_pickup_lists_items_with_lots_and_positive_quantities()
    {
        Assert.True(Valid(new PickupRequest([Item(), Item(Lot(5), Lot(7))], "Khách lấy tại quầy")));
        Assert.False(Valid(new PickupRequest([])));
        Assert.False(Valid(new PickupRequest([Item(Lot(0))])));
        Assert.False(Valid(new PickupRequest([Item(Lot(-1))])));
        Assert.False(Valid(new PickupRequest([new PickupItemRequest(Guid.NewGuid(), [])])));
        Assert.False(Valid(new PickupRequest([new PickupItemRequest(Guid.Empty, [Lot()])])));
        Assert.False(Valid(new PickupRequest([Item(new PickupLotRequest(Guid.Empty, 1))])));
        Assert.False(Valid(new PickupRequest([Item()], new string('n', 1001))));
    }

    [Fact]
    public void An_item_and_a_lot_may_appear_only_once()
    {
        var item = Item();
        var lot = Lot();

        Assert.False(Valid(new PickupRequest([item, item with { Lots = [Lot()] }])));
        Assert.False(Valid(new PickupRequest([Item(lot, lot with { BaseQuantity = 3 })])));
    }

    [Fact]
    public void A_pickup_is_bounded_in_items()
    {
        var many = Enumerable.Range(0, PickupRequestValidator.MaxItems + 1).Select(_ => Item()).ToList();

        Assert.False(Valid(new PickupRequest(many)));
        Assert.True(Valid(new PickupRequest(many.Take(PickupRequestValidator.MaxItems).ToList())));
    }

    [Fact]
    public void Cancelling_the_rest_needs_a_reason()
    {
        var validator = new CancelRemainingRequestValidator();

        Assert.True(validator.Validate(new CancelRemainingRequest("Khách không lấy nữa")).IsValid);
        Assert.False(validator.Validate(new CancelRemainingRequest("  ")).IsValid);
        Assert.False(validator.Validate(new CancelRemainingRequest(new string('r', 501))).IsValid);
    }

    [Fact]
    public void Cancelling_an_order_needs_a_reason()
    {
        var validator = new CancelOrderRequestValidator();

        Assert.True(validator.Validate(new CancelOrderRequest("Khách đổi ý")).IsValid);
        Assert.True(validator.Validate(new CancelOrderRequest(new string('r', 1000))).IsValid);
        Assert.False(validator.Validate(new CancelOrderRequest("  ")).IsValid);
        Assert.False(validator.Validate(new CancelOrderRequest(new string('r', 1001))).IsValid);
    }
}
