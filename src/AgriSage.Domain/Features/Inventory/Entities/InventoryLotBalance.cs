using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Quantity (base units) and carrying cost of one logical Lot. Changed only through InventoryLot.
// Invariants: on_hand >= 0, 0 <= reserved <= on_hand, total_cost_value >= 0, on_hand = 0 → total = 0.
public sealed class InventoryLotBalance : SoftDeletableChildEntity, IHasConcurrencyVersion
{
    private InventoryLotBalance()
    {
    }

    internal InventoryLotBalance(Guid inventoryLotId)
    {
        InventoryLotId = inventoryLotId;
    }

    public Guid InventoryLotId { get; private set; }

    public long QuantityOnHand { get; private set; }

    public long QuantityReserved { get; private set; }

    public decimal TotalCostValue { get; private set; }

    public long Version { get; private set; }

    public long AvailableQuantity => QuantityOnHand - QuantityReserved;

    // Current Weighted Average Cost per base unit; null when nothing is on hand.
    public decimal? AverageUnitCost =>
        QuantityOnHand > 0 ? CostRounding.RoundUnitCost(TotalCostValue / QuantityOnHand) : null;

    internal void Reserve(long quantity)
    {
        Guard.Positive(quantity);

        if (quantity > AvailableQuantity)
        {
            throw new DomainException($"Cannot reserve {quantity}; only {AvailableQuantity} available.");
        }

        QuantityReserved += quantity;
    }

    internal void ReleaseReservation(long quantity)
    {
        Guard.Positive(quantity);

        if (quantity > QuantityReserved)
        {
            throw new DomainException($"Cannot release {quantity}; only {QuantityReserved} reserved.");
        }

        QuantityReserved -= quantity;
    }

    internal LotBalanceChange Increase(long quantity, decimal unitCost)
    {
        Guard.Positive(quantity);
        Guard.UnitCost(unitCost);

        var totalCost = quantity * unitCost;

        QuantityOnHand = checked(QuantityOnHand + quantity);
        TotalCostValue += totalCost;

        return Snapshot(quantity, unitCost, totalCost);
    }

    // SALE (fromReserved) and ADJUSTMENT_OUT (unreserved stock) at current Weighted Average Cost.
    internal LotBalanceChange DecreaseAtAverageCost(long quantity, bool fromReserved)
    {
        Guard.Positive(quantity);
        EnsureCanDecrease(quantity, fromReserved);

        var unitCost = AverageUnitCost!.Value;

        return Decrease(quantity, unitCost, quantity * unitCost, fromReserved);
    }

    // Reverses an earlier inflow at that inflow's own unit cost; only unreserved stock can leave.
    internal LotBalanceChange DecreaseAtCost(long quantity, decimal unitCost)
    {
        Guard.Positive(quantity);
        Guard.UnitCost(unitCost);
        EnsureCanDecrease(quantity, fromReserved: false);

        return Decrease(quantity, unitCost, quantity * unitCost, fromReserved: false);
    }

    private void EnsureCanDecrease(long quantity, bool fromReserved)
    {
        var limit = fromReserved ? QuantityReserved : AvailableQuantity;

        if (quantity > limit)
        {
            var source = fromReserved ? "reserved" : "available";
            throw new DomainException($"Cannot issue {quantity}; only {limit} {source}.");
        }
    }

    private LotBalanceChange Decrease(long quantity, decimal unitCost, decimal totalCost, bool fromReserved)
    {
        // The issuance that empties the Lot takes the exact remaining cost (database design §35.2).
        if (quantity == QuantityOnHand)
        {
            totalCost = TotalCostValue;
        }
        else if (totalCost > TotalCostValue)
        {
            throw new DomainException("Issued cost cannot exceed the Lot's remaining cost value.");
        }

        QuantityOnHand -= quantity;

        if (fromReserved)
        {
            QuantityReserved -= quantity;
        }

        TotalCostValue = QuantityOnHand == 0 ? 0 : TotalCostValue - totalCost;

        return Snapshot(-quantity, unitCost, totalCost);
    }

    private LotBalanceChange Snapshot(long quantityDelta, decimal unitCost, decimal totalCost) =>
        new(quantityDelta, unitCost, totalCost, QuantityOnHand, TotalCostValue);
}
