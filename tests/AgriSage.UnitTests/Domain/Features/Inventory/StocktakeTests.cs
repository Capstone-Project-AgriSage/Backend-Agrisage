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
        var withCost = stocktake.AddItem(Guid.NewGuid(), systemQuantitySnapshot: 10, Now, unitCostSnapshot: 1_500.5m);
        var withoutCost = stocktake.AddItem(Guid.NewGuid(), systemQuantitySnapshot: 5, Now);
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
        var counted = stocktake.AddItem(Guid.NewGuid(), 10, Now);
        stocktake.AddItem(Guid.NewGuid(), 5, Now);
        stocktake.Start(StaffId, Now);
        stocktake.RecordCount(counted.Id, 10, StaffId, Now);

        Assert.Throws<DomainException>(() => stocktake.Complete(StaffId, Now));
        Assert.Equal(StocktakeStatus.InProgress, stocktake.Status);
    }

    [Fact]
    public void Full_lifecycle_draft_in_progress_completed()
    {
        var stocktake = CreateStocktake();
        var item = stocktake.AddItem(Guid.NewGuid(), 10, Now);

        Assert.Throws<DomainException>(() => stocktake.RecordCount(item.Id, 10, StaffId, Now));

        stocktake.Start(StaffId, Now);
        Assert.Throws<DomainException>(() => stocktake.AddItem(Guid.NewGuid(), 1, Now));

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

        stocktake.AddItem(lotId, 10, Now);
        Assert.Throws<DomainException>(() => stocktake.AddItem(lotId, 10, Now));
    }

    [Fact]
    public void Each_line_keeps_the_moment_its_snapshot_was_taken()
    {
        var stocktake = CreateStocktake();

        var item = stocktake.AddItem(Guid.NewGuid(), 10, Now.AddMinutes(-3));

        Assert.Equal(Now.AddMinutes(-3), item.SnapshotAt);
    }

    [Fact]
    public void Refreshing_a_stale_line_takes_a_new_snapshot_and_clears_only_its_count()
    {
        var stocktake = CreateStocktake();
        var stale = stocktake.AddItem(Guid.NewGuid(), 10, Now, 1_000m);
        var other = stocktake.AddItem(Guid.NewGuid(), 5, Now);
        stocktake.Start(StaffId, Now);
        stocktake.RecordCount(stale.Id, 8, StaffId, Now.AddMinutes(10), "LOST", "shelf 2");
        stocktake.RecordCount(other.Id, 5, StaffId, Now.AddMinutes(10));

        stocktake.RefreshItem(stale.Id, 9, Now.AddMinutes(20), 1_100m);

        Assert.Equal(9, stale.SystemQuantitySnapshot);
        Assert.Equal(1_100m, stale.UnitCostSnapshot);
        Assert.Equal(Now.AddMinutes(20), stale.SnapshotAt);
        Assert.Null(stale.CountedQuantity);
        Assert.Null(stale.DifferenceQuantity);
        Assert.Null(stale.DifferenceCostValue);
        Assert.Null(stale.CountedAt);
        Assert.Null(stale.ReasonCode);
        Assert.Equal(5, other.CountedQuantity);
        Assert.Throws<DomainException>(() => stocktake.Complete(StaffId, Now.AddMinutes(30)));

        stocktake.RecordCount(stale.Id, 9, StaffId, Now.AddMinutes(25));
        stocktake.Complete(StaffId, Now.AddMinutes(30));
        Assert.Equal(StocktakeStatus.Completed, stocktake.Status);
    }

    [Fact]
    public void Lines_are_refreshed_only_while_counting()
    {
        var stocktake = CreateStocktake();
        var item = stocktake.AddItem(Guid.NewGuid(), 10, Now);

        Assert.Throws<DomainException>(() => stocktake.RefreshItem(item.Id, 9, Now));

        stocktake.Start(StaffId, Now);
        Assert.Throws<DomainException>(() => stocktake.RefreshItem(Guid.NewGuid(), 9, Now));
        Assert.Throws<DomainException>(() => stocktake.RefreshItem(item.Id, -1, Now));
    }
}
