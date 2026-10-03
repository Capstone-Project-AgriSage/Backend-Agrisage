using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Order-level inventory reservation. Status is derived from item quantities (database design §35.3).
// Matching quantity_reserved changes on the Lot balances are applied by the calling use case;
// "one active reservation per Order" and "order item belongs to the Order" are checked by Application.
public sealed class InventoryReservation : SoftDeletableEntity
{
    private readonly List<InventoryReservationItem> _items = [];

    private InventoryReservation()
    {
    }

    public InventoryReservation(Guid storeId, Guid orderId, Guid reservedBy, DateTimeOffset reservedAt)
    {
        StoreId = storeId;
        OrderId = orderId;
        ReservedBy = reservedBy;
        ReservedAt = reservedAt;
        Status = InventoryReservationStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid OrderId { get; private set; }

    public InventoryReservationStatus Status { get; private set; }

    public DateTimeOffset ReservedAt { get; private set; }

    public Guid ReservedBy { get; private set; }

    // Most recent release action.
    public DateTimeOffset? ReleasedAt { get; private set; }

    public Guid? ReleasedBy { get; private set; }

    public string? ReleaseReason { get; private set; }

    public IReadOnlyCollection<InventoryReservationItem> Items => _items.AsReadOnly();

    public long RemainingQuantity => ActiveItems.Sum(i => i.RemainingQuantity);

    public InventoryReservationItem AddItem(Guid orderItemId, Guid inventoryLotId, long baseQuantity)
    {
        EnsureOpen();

        if (ActiveItems.Any(i => i.OrderItemId == orderItemId && i.InventoryLotId == inventoryLotId))
        {
            throw new DomainException("This order item already has a reservation line for the lot.");
        }

        var item = new InventoryReservationItem(Id, orderItemId, inventoryLotId, baseQuantity);
        _items.Add(item);
        RefreshStatus();

        return item;
    }

    // Reserves more for an order item in a lot: grows its line there, or adds the line (one line per order item and lot).
    // Used when the lot actually handed over is not the one reserved: the reservation moves to that lot, then is consumed.
    public InventoryReservationItem ReserveMore(Guid orderItemId, Guid inventoryLotId, long baseQuantity)
    {
        EnsureOpen();

        var existing = ActiveItems.FirstOrDefault(i => i.OrderItemId == orderItemId && i.InventoryLotId == inventoryLotId);
        if (existing is null)
        {
            return AddItem(orderItemId, inventoryLotId, baseQuantity);
        }

        existing.IncreaseReserved(baseQuantity);
        RefreshStatus();

        return existing;
    }

    public void Consume(Guid itemId, long quantity)
    {
        EnsureOpen();
        GetItem(itemId).Consume(quantity);
        RefreshStatus();
    }

    public void Release(Guid itemId, long quantity, Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();
        GetItem(itemId).Release(quantity);
        RecordRelease(releasedBy, releasedAt, reason);
        RefreshStatus();
    }

    public void ReleaseRemaining(Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();

        if (RemainingQuantity == 0)
        {
            throw new DomainException("Inventory reservation has no remaining quantity to release.");
        }

        ReleaseAllRemaining();
        RecordRelease(releasedBy, releasedAt, reason);
        RefreshStatus();
    }

    // Order cancelled before anything was consumed.
    public void Cancel(Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();

        if (ActiveItems.Any(i => i.BaseQuantityConsumed > 0))
        {
            throw new DomainException("A partially consumed inventory reservation cannot be cancelled; release the remainder instead.");
        }

        ReleaseAllRemaining();
        RecordRelease(releasedBy, releasedAt, reason);
        Status = InventoryReservationStatus.Cancelled;
    }

    // Reservations are released or cancelled, never deleted.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Inventory reservations cannot be deleted; release or cancel them.");

    private IEnumerable<InventoryReservationItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private InventoryReservationItem GetItem(Guid itemId) =>
        ActiveItems.SingleOrDefault(i => i.Id == itemId)
        ?? throw new DomainException($"Reservation line '{itemId}' was not found.");

    private void ReleaseAllRemaining()
    {
        foreach (var item in ActiveItems.Where(i => i.RemainingQuantity > 0))
        {
            item.Release(item.RemainingQuantity);
        }
    }

    private void RecordRelease(Guid releasedBy, DateTimeOffset releasedAt, string? reason)
    {
        ReleasedBy = releasedBy;
        ReleasedAt = releasedAt;
        ReleaseReason = reason;
    }

    private void EnsureOpen()
    {
        if (Status is not (InventoryReservationStatus.Active or InventoryReservationStatus.PartiallyConsumed))
        {
            throw new DomainException($"Inventory reservation is {Status} and can no longer change.");
        }
    }

    private void RefreshStatus()
    {
        var consumed = ActiveItems.Sum(i => i.BaseQuantityConsumed);

        Status = RemainingQuantity > 0
            ? consumed > 0 ? InventoryReservationStatus.PartiallyConsumed : InventoryReservationStatus.Active
            : consumed > 0 ? InventoryReservationStatus.Consumed : InventoryReservationStatus.Released;
    }
}
