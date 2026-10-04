using AgriSage.Application.Features.Orders;

namespace AgriSage.UnitTests.Application.Orders;

// Request validation of the quick counter sale (FLOW_1 section 9), before any database access.
public class CounterSaleValidatorTests
{
    private static CounterSaleItemRequest Line(long quantity = 5, IReadOnlyList<CounterSaleLotRequest>? lots = null, decimal? price = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), quantity, price, null, lots);

    private static CounterSaleLotRequest Lot(long quantity = 5) => new(Guid.NewGuid(), quantity);

    private static CounterSaleRequest Walk(params CounterSaleItemRequest[] items) => new("WALK_IN", items.Length == 0 ? [Line()] : items);

    private static List<string> Errors(CounterSaleRequest request) =>
        new CounterSaleRequestValidator().Validate(request).Errors.Select(e => e.PropertyName).ToList();

    [Fact]
    public void A_sale_needs_known_customer_data_and_lines_with_a_price_that_is_money()
    {
        Assert.Empty(Errors(Walk()));
        Assert.Empty(Errors(Walk(Line(lots: [Lot()]))));
        Assert.Empty(Errors(Walk() with { CustomerName = "   " }));
        Assert.Contains("CustomerType", Errors(Walk() with { CustomerType = "VIP" }));
        Assert.NotEmpty(Errors(Walk() with { Items = [] }));
        Assert.NotEmpty(Errors(Walk(Line(0))));
        Assert.NotEmpty(Errors(Walk(Line(OrderItemRequestValidator.MaxQuantity + 1))));
        Assert.NotEmpty(Errors(Walk(Line(price: 1.234m))));
        Assert.NotEmpty(Errors(Walk(Line(price: -1m))));
        Assert.Empty(Errors(Walk(Line(price: 0m))));
        Assert.NotEmpty(Errors(Walk() with { CustomerName = new string('N', 151) }));
        Assert.NotEmpty(Errors(Walk() with { Note = new string('n', 1001) }));
    }

    [Fact]
    public void A_registered_sale_needs_a_farmer()
    {
        var registered = Walk() with { CustomerType = "REGISTERED" };

        Assert.Contains("FarmerProfileId", Errors(registered));
        Assert.Contains("FarmerProfileId", Errors(registered with { FarmerProfileId = Guid.Empty }));
        Assert.Empty(Errors(registered with { FarmerProfileId = Guid.NewGuid() }));
    }

    [Fact]
    public void Lines_are_unique_per_pair_and_bounded()
    {
        var line = Line();
        var many = Enumerable.Range(0, CounterSaleRequestValidator.MaxItems + 1).Select(_ => Line()).ToArray();

        Assert.NotEmpty(Errors(Walk(line, line with { Quantity = 2 })));
        Assert.NotEmpty(Errors(Walk(many)));
        Assert.Empty(Errors(Walk(many.Take(CounterSaleRequestValidator.MaxItems).ToArray())));
    }

    [Fact]
    public void Lots_are_positive_and_each_lot_appears_once_per_line_but_are_optional_for_the_preview()
    {
        var lot = Lot();

        Assert.Empty(Errors(Walk(Line(lots: [Lot(3), Lot(2)]))));
        Assert.NotEmpty(Errors(Walk(Line(lots: [lot, lot with { BaseQuantity = 1 }]))));
        Assert.NotEmpty(Errors(Walk(Line(lots: [Lot(0)]))));
        Assert.NotEmpty(Errors(Walk(Line(lots: [new CounterSaleLotRequest(Guid.Empty, 1)]))));
    }
}
