using AgriSage.Application.Features.Returns;

namespace AgriSage.UnitTests.Application.Returns;

// What a farmer sees of a return: everything the staff see, except what the goods cost the store.
public class FarmerReturnViewTests
{
    private static SalesReturnItemResponse Item(decimal cost, decimal costValue) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(),
        5, 12_000m, 1, 60_000m, cost, costValue, "DAMAGED_PRODUCT", "DAMAGED", "RESTOCK", "note", null, null);

    private static SalesReturnResponse Return(params SalesReturnItemResponse[] items) => new(
        Guid.NewGuid(), Guid.NewGuid(), "RT-1", Guid.NewGuid(), "OD-1", Guid.NewGuid(), "Nông dân A", "REQUESTED",
        Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, null, null, "reason", "note",
        60_000m, 0m, 60_000m, items, []);

    [Fact]
    public void A_return_loses_the_cost_of_every_line_and_keeps_everything_else()
    {
        var first = Item(7_000m, 35_000m);
        var second = Item(8_000m, 40_000m);
        var staffView = Return(first, second);

        var farmerView = FarmerReturnView.Redact(staffView);

        Assert.All(farmerView.Items, i =>
        {
            Assert.Null(i.OriginalCogsUnitCost);
            Assert.Null(i.ReturnInventoryCostValue);
        });
        Assert.Equal(first with { OriginalCogsUnitCost = null, ReturnInventoryCostValue = null }, farmerView.Items[0]);
        Assert.Equal(second with { OriginalCogsUnitCost = null, ReturnInventoryCostValue = null }, farmerView.Items[1]);
        Assert.Equal(staffView with { Items = farmerView.Items }, farmerView);
    }

    [Fact]
    public void Redacting_does_not_change_the_response_the_staff_get()
    {
        var staffView = Return(Item(7_000m, 35_000m));

        _ = FarmerReturnView.Redact(staffView);

        Assert.Equal(7_000m, staffView.Items[0].OriginalCogsUnitCost);
        Assert.Equal(35_000m, staffView.Items[0].ReturnInventoryCostValue);
    }

    [Fact]
    public void The_returnable_list_loses_the_cost_of_every_source_and_keeps_the_quantities()
    {
        var source = new ReturnableSource(null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "LOT-1", new DateOnly(2027, 1, 1),
            10, 2, 8, 12_000m, 1, 7_000m);
        var item = new ReturnableOrderItem(Guid.NewGuid(), "SKU-1", "Phân NPK", 10, 2, 8, 12_000m, 1, [source, source with { OriginalCogsUnitCost = 9_000m }]);
        var staffView = new ReturnableResponse(Guid.NewGuid(), "OD-1", "DELIVERY", [item]);

        var farmerView = FarmerReturnView.Redact(staffView);

        var sources = farmerView.Items.Single().Sources;
        Assert.Equal(2, sources.Count);
        Assert.All(sources, s => Assert.Null(s.OriginalCogsUnitCost));
        Assert.All(sources, s => Assert.Equal(8, s.ReturnableBaseQuantity));
        Assert.Equal("LOT-1", sources[0].LotNumber);
        Assert.Equal(7_000m, staffView.Items.Single().Sources[0].OriginalCogsUnitCost);
    }
}
