using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Lot-level quantity and cost of one Stock Movement. Created only through StockMovement.
public sealed class StockMovementItem : SoftDeletableChildEntity
{
    private StockMovementItem()
    {
    }

    internal StockMovementItem(Guid stockMovementId, Guid inventoryLotId, LotBalanceChange change, string? note)
    {
        StockMovementId = stockMovementId;
        InventoryLotId = inventoryLotId;
        QuantityDeltaBase = change.QuantityDelta;
        UnitCostSnapshot = Guard.UnitCost(change.UnitCost);
        TotalCostSnapshot = Guard.NotNegative(change.TotalCost);
        QuantityOnHandAfter = change.QuantityOnHandAfter;
        TotalCostValueAfter = change.TotalCostValueAfter;
        Note = note;
    }

    public Guid StockMovementId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    // Positive = stock increase, negative = stock decrease.
    public long QuantityDeltaBase { get; private set; }

    public decimal UnitCostSnapshot { get; private set; }

    public decimal TotalCostSnapshot { get; private set; }

    public long? QuantityOnHandAfter { get; private set; }

    public decimal? TotalCostValueAfter { get; private set; }

    public string? Note { get; private set; }
}
