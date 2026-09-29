using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Lot-level reserved base quantity for one Order Item. Changed only through InventoryReservation.
// Invariant: consumed + released <= reserved.
public sealed class InventoryReservationItem : SoftDeletableChildEntity
{
    private InventoryReservationItem()
    {
    }

    internal InventoryReservationItem(
        Guid inventoryReservationId,
        Guid orderItemId,
        Guid inventoryLotId,
        long baseQuantityReserved)
    {
        InventoryReservationId = inventoryReservationId;
        OrderItemId = orderItemId;
        InventoryLotId = inventoryLotId;
        BaseQuantityReserved = Guard.Positive(baseQuantityReserved);
    }

    public Guid InventoryReservationId { get; private set; }

    public Guid OrderItemId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    public long BaseQuantityReserved { get; private set; }

    public long BaseQuantityConsumed { get; private set; }

    public long BaseQuantityReleased { get; private set; }

    public long RemainingQuantity => BaseQuantityReserved - BaseQuantityConsumed - BaseQuantityReleased;

    internal void Consume(long quantity)
    {
        EnsureWithinRemaining(quantity);
        BaseQuantityConsumed += quantity;
    }

    internal void Release(long quantity)
    {
        EnsureWithinRemaining(quantity);
        BaseQuantityReleased += quantity;
    }

    private void EnsureWithinRemaining(long quantity)
    {
        Guard.Positive(quantity);

        if (quantity > RemainingQuantity)
        {
            throw new DomainException($"Quantity {quantity} exceeds the remaining reserved quantity {RemainingQuantity}.");
        }
    }
}
