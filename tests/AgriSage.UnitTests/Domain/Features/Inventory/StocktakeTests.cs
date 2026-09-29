using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

public class StocktakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static Stocktake CreateStocktake() => new(Guid.NewGuid(), "ST-0001", StaffId);

    [Fact]
    public void Counting_computes_quantity_and_cost_difference()
    {
        var stocktake = CreateStocktake();
        var withCost = stocktake.AddItem(Guid.NewGuid(), systemQuantitySnapshot: 10, unitCostSnapshot: 1_500.5m);
        var withoutCost = stocktake.AddItem(Guid.NewGuid(), systemQuantitySnapshot: 5);
        stocktake.Start(StaffId, Now);

        stocktake.RecordCount(withCost.Id, 7, StaffId, Now, "DAMAGED");
        stocktake.RecordCount(withoutCost.Id, 6, StaffId, Now);

        Assert.Equal(-3, withCost.DifferenceQuantity);
        Assert.Equal(-4_501.5m, withCost.DifferenceCostValue);
        Assert.Equal(1, withoutCost.DifferenceQuantity);
        Assert.Null(withoutCost.DifferenceCostValue);
    }

    [Fact]
    public void Completion_requires_every_line_to_be_counted()
    {
        var stocktake = CreateStocktake();
        var counted = stocktake.AddItem(Guid.NewGuid(), 10);
        stocktake.AddItem(Guid.NewGuid(), 5);
        stocktake.Start(StaffId, Now);
        stocktake.RecordCount(counted.Id, 10, StaffId, Now);

        Assert.Throws<DomainException>(() => stocktake.Complete(StaffId, Now));
        Assert.Equal(StocktakeStatus.InProgress, stocktake.Status);
    }

    [Fact]
    public void Full_lifecycle_draft_in_progress_completed()
    {
        var stocktake = CreateStocktake();
        var item = stocktake.AddItem(Guid.NewGuid(), 10);

        Assert.Throws<DomainException>(() => stocktake.RecordCount(item.Id, 10, StaffId, Now));

        stocktake.Start(StaffId, Now);
        Assert.Throws<DomainException>(() => stocktake.AddItem(Guid.NewGuid(), 1));

        stocktake.RecordCount(item.Id, 10, StaffId, Now);
        stocktake.Complete(StaffId, Now);

        Assert.Equal(StocktakeStatus.Completed, stocktake.Status);
        Assert.Throws<DomainException>(stocktake.Cancel);
    }

    [Fact]
    public void Same_lot_cannot_be_added_twice_and_empty_stocktake_cannot_start()
    {
        var stocktake = CreateStocktake();
        var lotId = Guid.NewGuid();

        Assert.Throws<DomainException>(() => stocktake.Start(StaffId, Now));

        stocktake.AddItem(lotId, 10);
        Assert.Throws<DomainException>(() => stocktake.AddItem(lotId, 10));
    }
}
