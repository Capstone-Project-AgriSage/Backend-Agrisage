using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// Lot physically prepared for a Delivery Item. Changed only through Delivery.
// Invariant: delivered + released <= allocated. Status is derived like reservations (database design §35.3):
// DELIVERED if anything was delivered once nothing remains, RELEASED for a lot swap, CANCELLED for a cancelled Delivery.
public sealed class DeliveryItemLotAllocation : SoftDeletableChildEntity
{
    private DeliveryItemLotAllocation()
    {
    }

    internal DeliveryItemLotAllocation(
        Guid deliveryItemId,
        Guid inventoryLotId,
        long allocatedBaseQuantity,
        Guid? inventoryReservationItemId)
    {
        DeliveryItemId = deliveryItemId;
        InventoryLotId = inventoryLotId;
        InventoryReservationItemId = inventoryReservationItemId;
        AllocatedBaseQuantity = Guard.Positive(allocatedBaseQuantity);
        Status = DeliveryLotAllocationStatus.Allocated;
    }

    public Guid DeliveryItemId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    public Guid? InventoryReservationItemId { get; private set; }

    public long AllocatedBaseQuantity { get; private set; }

    public long DeliveredBaseQuantity { get; private set; }

    public long ReleasedBaseQuantity { get; private set; }

    public DeliveryLotAllocationStatus Status { get; private set; }

    public long UndeliveredQuantity => AllocatedBaseQuantity - DeliveredBaseQuantity - ReleasedBaseQuantity;

    // Allocated quantity still committed to this Delivery (delivered or waiting to be delivered).
    public long CommittedQuantity => AllocatedBaseQuantity - ReleasedBaseQuantity;

    internal void RecordDelivered(long quantity)
    {
        if (quantity == 0)
        {
            return;
        }

        Guard.Positive(quantity);

        if (quantity > UndeliveredQuantity)
        {
            throw new DomainException($"Cannot deliver {quantity}; only {UndeliveredQuantity} remain on this lot allocation.");
        }

        DeliveredBaseQuantity += quantity;
        Status = UndeliveredQuantity > 0
            ? DeliveryLotAllocationStatus.PartiallyDelivered
            : DeliveryLotAllocationStatus.Delivered;
    }

    internal void ReleaseRemaining(bool deliveryCancelled)
    {
        if (UndeliveredQuantity == 0)
        {
            throw new DomainException("Lot allocation has nothing left to release.");
        }

        ReleasedBaseQuantity += UndeliveredQuantity;

        Status = DeliveredBaseQuantity > 0
            ? DeliveryLotAllocationStatus.Delivered
            : deliveryCancelled
                ? DeliveryLotAllocationStatus.Cancelled
                : DeliveryLotAllocationStatus.Released;
    }
}
