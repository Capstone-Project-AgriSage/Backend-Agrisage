using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Inventory.Entities;

// One counted Lot in a Stocktake. Created, counted and refreshed only through Stocktake.
// snapshot_at = when the system quantity was taken (database design §35.19): a line is stale when a stock movement
// for its Lot was posted between the snapshot and the count.
public sealed class StocktakeItem : SoftDeletableChildEntity
{
    private StocktakeItem()
    {
    }

    internal StocktakeItem(
        Guid stocktakeId,
        Guid inventoryLotId,
        long systemQuantitySnapshot,
        DateTimeOffset snapshotAt,
        decimal? unitCostSnapshot)
    {
        StocktakeId = stocktakeId;
        InventoryLotId = inventoryLotId;
        SystemQuantitySnapshot = Guard.NotNegative(systemQuantitySnapshot);
        SnapshotAt = snapshotAt;
        UnitCostSnapshot = unitCostSnapshot is null ? null : Guard.UnitCost(unitCostSnapshot.Value);
    }

    public Guid StocktakeId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    public long SystemQuantitySnapshot { get; private set; }

    public DateTimeOffset SnapshotAt { get; private set; }

    public long? CountedQuantity { get; private set; }

    public long? DifferenceQuantity { get; private set; }

    public decimal? UnitCostSnapshot { get; private set; }

    public decimal? DifferenceCostValue { get; private set; }

    public Guid? CountedBy { get; private set; }

    public DateTimeOffset? CountedAt { get; private set; }

    // Reason code values are examples only in the database design.
    public string? ReasonCode { get; private set; }

    public string? Note { get; private set; }

    public bool IsCounted => CountedQuantity is not null;

    internal void RecordCount(
        long countedQuantity,
        Guid countedBy,
        DateTimeOffset countedAt,
        string? reasonCode,
        string? note)
    {
        CountedQuantity = Guard.NotNegative(countedQuantity);
        DifferenceQuantity = countedQuantity - SystemQuantitySnapshot;
        DifferenceCostValue = UnitCostSnapshot is null ? null : DifferenceQuantity * UnitCostSnapshot;
        CountedBy = countedBy;
        CountedAt = countedAt;
        ReasonCode = reasonCode;
        Note = note;
    }

    // Takes a new snapshot of a stale line and clears its count, so only this Lot is counted again.
    internal void Refresh(long systemQuantitySnapshot, decimal? unitCostSnapshot, DateTimeOffset snapshotAt)
    {
        SystemQuantitySnapshot = Guard.NotNegative(systemQuantitySnapshot);
        UnitCostSnapshot = unitCostSnapshot is null ? null : Guard.UnitCost(unitCostSnapshot.Value);
        SnapshotAt = snapshotAt;
        CountedQuantity = null;
        DifferenceQuantity = null;
        DifferenceCostValue = null;
        CountedBy = null;
        CountedAt = null;
        ReasonCode = null;
        Note = null;
    }
}
