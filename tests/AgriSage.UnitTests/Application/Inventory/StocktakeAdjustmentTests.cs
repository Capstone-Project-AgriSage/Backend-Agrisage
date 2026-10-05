using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Stocktakes;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Entities;

namespace AgriSage.UnitTests.Application.Inventory;

public class StocktakeAdjustmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_lot_count_accepts_a_cost_and_recount_refresh_recalculate_the_difference()
    {
        var actor = Guid.NewGuid();
        var s = new Stocktake(Guid.NewGuid(), "ST-test", actor);
        var i = s.AddItem(Guid.NewGuid(), 0, Now);
        s.Start(actor, Now);
        s.RecordCount(i.Id, 5, actor, Now, "OTHER", unitCost: 12.345678m);
        Assert.Equal(12.345678m, i.UnitCostSnapshot);
        Assert.Equal(61.728390m, i.DifferenceCostValue);
        s.RecordCount(i.Id, 6, actor, Now, "OTHER", unitCost: 10m);
        Assert.Equal(60m, i.DifferenceCostValue);
        s.RefreshItem(i.Id, 0, Now.AddMinutes(10));
        Assert.Null(i.UnitCostSnapshot);
        Assert.Null(i.DifferenceCostValue);
        Assert.Null(i.CountedQuantity);
    }

    [Fact]
    public void Existing_stock_keeps_its_snapshot_cost_when_client_sends_another_cost()
    {
        var actor = Guid.NewGuid();
        var s = new Stocktake(Guid.NewGuid(), "ST-test", actor);
        var i = s.AddItem(Guid.NewGuid(), 10, Now, 20m);
        s.Start(actor, Now);
        s.RecordCount(i.Id, 12, actor, Now, "OTHER", unitCost: 999m);
        Assert.Equal(20m, i.UnitCostSnapshot);
        Assert.Equal(40m, i.DifferenceCostValue);
    }

    [Fact]
    public void Empty_lot_cost_rejects_negative_values_and_more_than_six_decimals()
    {
        var actor = Guid.NewGuid();
        var s = new Stocktake(Guid.NewGuid(), "ST-test", actor);
        var i = s.AddItem(Guid.NewGuid(), 0, Now);
        s.Start(actor, Now);
        Assert.Throws<DomainException>(() => s.RecordCount(i.Id, 1, actor, Now, unitCost: -1));
        Assert.Throws<DomainException>(() => s.RecordCount(i.Id, 1, actor, Now, unitCost: 1.0000001m));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5)]
    public void Manual_adjustment_accepts_either_single_direction(long delta)
    {
        var request = new StockAdjustmentRequest(" damaged ", "Explanation", [new(Guid.NewGuid(), delta, 1.123456m)]);
        Assert.True(new StockAdjustmentRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Manual_adjustment_rejects_mixed_zero_duplicate_and_unrepresentable_deltas()
    {
        var validator = new StockAdjustmentRequestValidator();
        var id = Guid.NewGuid();
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(id, 1), new(Guid.NewGuid(), -1)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(id, 0)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(id, long.MinValue)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(id, 1), new(id, 2)])).IsValid);
    }

    [Fact]
    public void Manual_adjustment_requires_a_note_valid_reason_and_valid_cost()
    {
        var validator = new StockAdjustmentRequestValidator();
        Assert.False(validator.Validate(new StockAdjustmentRequest("STOCKTAKE_DIFFERENCE", "note", [new(Guid.NewGuid(), 1)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", " ", [new(Guid.NewGuid(), 1)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", new string('x', 1001), [new(Guid.NewGuid(), 1)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(Guid.NewGuid(), 1, -1)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [new(Guid.NewGuid(), 1, 1.0000001m)])).IsValid);
        Assert.False(validator.Validate(new StockAdjustmentRequest("OTHER", "note", [])).IsValid);
    }

    [Fact]
    public void Stocktake_count_validation_checks_ids_quantities_costs_and_line_limits()
    {
        var validator = new StocktakeCountsRequestValidator();
        var id = Guid.NewGuid();
        Assert.True(validator.Validate(new StocktakeCountsRequest([new(id, 0, 0, "stocktake_difference")])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([new(id, -1)])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([new(id, 1, 1.0000001m)])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([new(id, 1, ReasonCode: "BAD")])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([new(id, 1, Note: new string('x', 501))])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([new(id, 1), new(id, 2)])).IsValid);
        Assert.False(validator.Validate(new StocktakeCountsRequest([])).IsValid);
    }

    [Fact]
    public void Stocktake_creation_and_lists_validate_scope_notes_and_dates()
    {
        var id = Guid.NewGuid();
        Assert.True(new CreateStocktakeRequestValidator().Validate(new CreateStocktakeRequest()).IsValid);
        Assert.False(new CreateStocktakeRequestValidator().Validate(new CreateStocktakeRequest([id, id])).IsValid);
        Assert.False(new CreateStocktakeRequestValidator().Validate(new CreateStocktakeRequest([Guid.Empty])).IsValid);
        Assert.False(new CreateStocktakeRequestValidator().Validate(new CreateStocktakeRequest(Note: new string('x', 1001))).IsValid);
        Assert.False(new StocktakeListRequestValidator().Validate(new StocktakeListRequest { Status = "BAD" }).IsValid);
        Assert.False(new StocktakeListRequestValidator().Validate(new StocktakeListRequest { PageSize = 101 }).IsValid);
        Assert.False(new StocktakeListRequestValidator().Validate(new StocktakeListRequest
        {
            FromDate = new(2026, 10, 5),
            ToDate = new(2026, 10, 4)
        }).IsValid);
    }
}
