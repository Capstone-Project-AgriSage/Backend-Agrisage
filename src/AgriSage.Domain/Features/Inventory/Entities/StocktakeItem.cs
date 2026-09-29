using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Inventory.Entities;

// One counted Lot in a Stocktake. Created and counted only through Stocktake.
public sealed class StocktakeItem : SoftDeletableChildEntity
{
    private StocktakeItem()
    {
    }

    internal StocktakeItem(Guid stocktakeId, Guid inventoryLotId, long systemQuantitySnapshot, decimal? unitCostSnapshot)
    {
        StocktakeId = stocktakeId;
        InventoryLotId = inventoryLotId;
        SystemQuantitySnapshot = Guard.NotNegative(systemQuantitySnapshot);
        UnitCostSnapshot = unitCostSnapshot is null ? null : Guard.UnitCost(unitCostSnapshot.Value);
    }

    public Guid StocktakeId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    public long SystemQuantitySnapshot { get; private set; }

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
}
