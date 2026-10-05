using AgriSage.Application.Features.Reports;

namespace AgriSage.UnitTests.Application.Inventory;

public class InventoryReportValidationTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("2026-10-05", "2026-10-04", false)]
    [InlineData("2026-01-01", "2027-01-01", true)]
    [InlineData("2026-01-01", "2027-01-02", false)]
    [InlineData("2026-10-05", "2026-10-05", true)]
    public void Period_is_required_ordered_and_limited_to_366_inclusive_Vietnam_days(string? from, string? to, bool valid)
    {
        var request = new InventoryMovementReportRequest
        {
            FromDate = from is null ? null : DateOnly.Parse(from),
            ToDate = to is null ? null : DateOnly.Parse(to)
        };
        Assert.Equal(valid, new InventoryMovementReportRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Product_is_required_and_optional_ids_cannot_be_empty()
    {
        var card = new StockCardRequest { FromDate = new(2026, 10, 5), ToDate = new(2026, 10, 5) };
        Assert.False(new StockCardRequestValidator().Validate(card).IsValid);
        Assert.False(new StockCardRequestValidator().Validate(card with { StoreProductId = Guid.NewGuid(), InventoryLotId = Guid.Empty }).IsValid);
        Assert.True(new StockCardRequestValidator().Validate(card with { StoreProductId = Guid.NewGuid() }).IsValid);
        Assert.False(new InventoryValuationReportRequestValidator().Validate(new InventoryValuationReportRequest { CategoryId = Guid.Empty }).IsValid);
        Assert.True(new InventoryValuationReportRequestValidator().Validate(new InventoryValuationReportRequest()).IsValid);
    }
}
