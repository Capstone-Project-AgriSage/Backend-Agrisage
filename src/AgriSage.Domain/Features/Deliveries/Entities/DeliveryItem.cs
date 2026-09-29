using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// Order Item quantity planned for one Delivery. Changed only through Delivery.
// Invariant: delivered + cancelled <= planned base quantity. Status is derived (database design §35.4).
public sealed class DeliveryItem : SoftDeletableChildEntity
{
    private readonly List<DeliveryItemLotAllocation> _lotAllocations = [];

    private DeliveryItem()
    {
    }

    internal DeliveryItem(Guid deliveryId, Guid orderItemId, long plannedQuantity, long conversionToBaseSnapshot)
    {
        DeliveryId = deliveryId;
        OrderItemId = orderItemId;
        PlannedQuantity = Guard.Positive(plannedQuantity);
        ConversionToBaseSnapshot = Guard.Positive(conversionToBaseSnapshot);
        PlannedBaseQuantity = checked(PlannedQuantity * ConversionToBaseSnapshot);
        Status = DeliveryItemStatus.Pending;
    }

    public Guid DeliveryId { get; private set; }

    public Guid OrderItemId { get; private set; }

    public long PlannedQuantity { get; private set; }

    public long ConversionToBaseSnapshot { get; private set; }

    public long PlannedBaseQuantity { get; private set; }

    public long DeliveredBaseQuantity { get; private set; }

    public long CancelledBaseQuantity { get; private set; }

    public DeliveryItemStatus Status { get; private set; }

    public IReadOnlyCollection<DeliveryItemLotAllocation> LotAllocations => _lotAllocations.AsReadOnly();

    public long RemainingBaseQuantity => PlannedBaseQuantity - DeliveredBaseQuantity - CancelledBaseQuantity;

    internal IEnumerable<DeliveryItemLotAllocation> ActiveAllocations => _lotAllocations.Where(a => !a.IsDeleted);

    internal DeliveryItemLotAllocation AddAllocation(
        Guid inventoryLotId,
        long allocatedBaseQuantity,
        Guid? inventoryReservationItemId)
    {
        var committed = ActiveAllocations.Sum(a => a.CommittedQuantity);

        if (committed + allocatedBaseQuantity > PlannedBaseQuantity - CancelledBaseQuantity)
        {
            throw new DomainException("Lot allocations cannot exceed the planned base quantity of the delivery item.");
        }

        var allocation = new DeliveryItemLotAllocation(Id, inventoryLotId, allocatedBaseQuantity, inventoryReservationItemId);
        _lotAllocations.Add(allocation);

        return allocation;
    }

    internal void RecordDelivered(long quantity)
    {
        if (quantity == 0)
        {
            return;
        }

        if (quantity > RemainingBaseQuantity)
        {
            throw new DomainException($"Cannot deliver {quantity}; only {RemainingBaseQuantity} remain on this delivery item.");
        }

        DeliveredBaseQuantity += quantity;
        RefreshStatus();
    }

    internal void CancelRemaining()
    {
        if (RemainingBaseQuantity == 0)
        {
            return;
        }

        CancelledBaseQuantity += RemainingBaseQuantity;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (RemainingBaseQuantity > 0)
        {
            Status = DeliveredBaseQuantity > 0 ? DeliveryItemStatus.PartiallyDelivered : DeliveryItemStatus.Pending;
        }
        else if (CancelledBaseQuantity == 0)
        {
            Status = DeliveryItemStatus.Delivered;
        }
        else
        {
            Status = DeliveredBaseQuantity == 0 ? DeliveryItemStatus.Cancelled : DeliveryItemStatus.PartiallyCancelled;
        }
    }
}
