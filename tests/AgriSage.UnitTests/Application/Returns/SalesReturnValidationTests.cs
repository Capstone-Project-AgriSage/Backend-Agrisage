using AgriSage.Application.Features.Returns;

namespace AgriSage.UnitTests.Application.Returns;

public class SalesReturnValidationTests
{
    private static ReturnItemRequest Line => new(Guid.NewGuid(), 1, "OTHER", OriginalStockMovementItemId: Guid.NewGuid());
    [Fact]
    public void Sources_require_exactly_one_nonempty_id_and_positive_base_quantity()
    {
        var validator = new ReturnItemRequestValidator();
        Assert.True(validator.Validate(Line).IsValid);
        Assert.True(validator.Validate(Line with { OriginalStockMovementItemId = null, DeliveryItemLotAllocationId = Guid.NewGuid() }).IsValid);
        Assert.False(validator.Validate(Line with { OriginalStockMovementItemId = null }).IsValid);
        Assert.False(validator.Validate(Line with { DeliveryItemLotAllocationId = Guid.NewGuid() }).IsValid);
        Assert.False(validator.Validate(Line with { OriginalStockMovementItemId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(Line with { ReturnedBaseQuantity = 0 }).IsValid);
        Assert.False(validator.Validate(Line with { ReasonCode = "LOST" }).IsValid);
        Assert.True(validator.Validate(Line with { ReasonCode = " damaged_product " }).IsValid);
    }
    [Fact]
    public void Requests_validate_required_items_reasons_dates_and_inspection_conditions()
    {
        var validator = new CreateReturnRequestValidator();
        Assert.True(validator.Validate(new CreateReturnRequest(Guid.NewGuid(), [Line])).IsValid);
        Assert.False(validator.Validate(new CreateReturnRequest(Guid.Empty, [])).IsValid);
        Assert.False(validator.Validate(new CreateReturnRequest(Guid.NewGuid(), [null!])).IsValid);
        Assert.False(validator.Validate(new CreateReturnRequest(Guid.NewGuid(), [Line], Note: new string('n', 1001))).IsValid);
        Assert.False(new ReturnReasonRequestValidator().Validate(new ReturnReasonRequest(" ")).IsValid);
        var inspection = new ReturnInspectionRequestValidator();
        foreach (var value in new[] { "RESELLABLE", "damaged", "EXPIRED", "UNUSABLE" }) Assert.True(inspection.Validate(new ReturnInspectionRequest(value)).IsValid);
        foreach (var value in new[] { "PENDING_INSPECTION", "RESTOCK", "0", "" }) Assert.False(inspection.Validate(new ReturnInspectionRequest(value)).IsValid);
        var list = new SalesReturnListRequestValidator();
        Assert.True(list.Validate(new SalesReturnListRequest() { Status = "PARTIALLY_RESOLVED" }).IsValid);
        Assert.False(list.Validate(new SalesReturnListRequest() { Page = 0, Status = "NO", ToDate = DateOnly.MaxValue }).IsValid);
        Assert.False(list.Validate(new SalesReturnListRequest() { FromDate = new(2026, 10, 5), ToDate = new(2026, 10, 4) }).IsValid);
    }
}

