using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Stores.Entities;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// Delivery Note of a DELIVERY Order; aggregate root for Items, Lot Allocations, Attempts and Attempt Items.
// Lifecycle (database design §35.5): DRAFT → ASSIGNED → OUT_FOR_DELIVERY, then after each attempt
// DELIVERED / PARTIALLY_DELIVERED / RETRY_PENDING; the last two can be dispatched again.
// SALE stock posting, reservation consumption, Order updates and debt creation are done by the fulfillment use case;
// "planned across all Deliveries <= Order Item remaining" is checked there as well.
public sealed class Delivery : SoftDeletableEntity
{
    private readonly List<DeliveryItem> _items = [];
    private readonly List<DeliveryAttempt> _attempts = [];

    private Delivery()
    {
    }

    public Delivery(
        Order order,
        string deliveryNumber,
        DeliveryAddress address,
        Guid createdBy,
        DateTimeOffset? scheduledAt = null,
        string? note = null)
    {
        if (order.FulfillmentType != FulfillmentType.Delivery)
        {
            throw new DomainException($"Order '{order.OrderNumber}' is not a DELIVERY order.");
        }

        address.Validate();

        StoreId = order.StoreId;
        OrderId = order.Id;
        DeliveryNumber = Guard.NotNullOrWhiteSpace(deliveryNumber);
        RecipientNameSnapshot = address.RecipientName;
        RecipientPhoneSnapshot = address.RecipientPhone;
        AddressLineSnapshot = address.AddressLine;
        WardSnapshot = address.Ward;
        DistrictSnapshot = address.District;
        ProvinceSnapshot = address.Province;
        LatitudeSnapshot = address.Latitude;
        LongitudeSnapshot = address.Longitude;
        CreatedBy = createdBy;
        ScheduledAt = scheduledAt;
        Note = note;
        Status = DeliveryStatus.Draft;
    }

    public Guid StoreId { get; private set; }

    public Guid OrderId { get; private set; }

    public string DeliveryNumber { get; private set; } = null!;

    public Guid? AssignedToMemberId { get; private set; }

    public StoreMember? AssignedToMember { get; private set; }

    public string RecipientNameSnapshot { get; private set; } = null!;

    public string RecipientPhoneSnapshot { get; private set; } = null!;

    public string AddressLineSnapshot { get; private set; } = null!;

    public string? WardSnapshot { get; private set; }

    public string? DistrictSnapshot { get; private set; }

    public string ProvinceSnapshot { get; private set; } = null!;

    public decimal? LatitudeSnapshot { get; private set; }

    public decimal? LongitudeSnapshot { get; private set; }

    public DateTimeOffset? ScheduledAt { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public string? Note { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<DeliveryItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<DeliveryAttempt> Attempts => _attempts.AsReadOnly();

    public long RemainingBaseQuantity => ActiveItems.Sum(i => i.RemainingBaseQuantity);

    public DeliveryItem AddItem(OrderItem orderItem, long plannedQuantity)
    {
        EnsureStatus(DeliveryStatus.Draft, DeliveryStatus.Assigned);

        if (orderItem.OrderId != OrderId || orderItem.IsDeleted)
        {
            throw new DomainException("The order item does not belong to this delivery's order.");
        }

        var item = new DeliveryItem(Id, orderItem.Id, plannedQuantity, orderItem.ConversionToBaseSnapshot);

        if (item.PlannedBaseQuantity > orderItem.RemainingBaseQuantity)
        {
            throw new DomainException(
                $"Planned {item.PlannedBaseQuantity} base units exceed the order item's remaining {orderItem.RemainingBaseQuantity}.");
        }

        _items.Add(item);

        return item;
    }

    public DeliveryItemLotAllocation AllocateLot(
        Guid deliveryItemId,
        Guid inventoryLotId,
        long allocatedBaseQuantity,
        Guid? inventoryReservationItemId = null)
    {
        EnsureNotOutForDelivery();
        return GetItem(deliveryItemId).AddAllocation(inventoryLotId, allocatedBaseQuantity, inventoryReservationItemId);
    }

    // Changing the actual Lot before dispatch = release this allocation, then AllocateLot the replacement.
    public void ReleaseAllocation(Guid allocationId)
    {
        EnsureNotOutForDelivery();
        GetAllocation(allocationId).ReleaseRemaining(deliveryCancelled: false);
    }

    public void Assign(Guid storeMemberId)
    {
        EnsureNotOutForDelivery();

        AssignedToMemberId = storeMemberId;

        if (Status == DeliveryStatus.Draft)
        {
            Status = DeliveryStatus.Assigned;
        }
    }

    public void Dispatch(DateTimeOffset dispatchedAt)
    {
        EnsureStatus(DeliveryStatus.Assigned, DeliveryStatus.PartiallyDelivered, DeliveryStatus.RetryPending);

        if (AssignedToMemberId is null)
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' has no assigned delivery staff.");
        }

        if (!ActiveAllocations.Any(a => a.UndeliveredQuantity > 0))
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' has no allocated goods left to deliver.");
        }

        Status = DeliveryStatus.OutForDelivery;
        DispatchedAt = dispatchedAt;
    }

    // attemptedQuantities: lot allocation id → attempted base quantity.
    public DeliveryAttempt StartAttempt(
        Guid attemptedByMemberId,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<Guid, long> attemptedQuantities)
    {
        EnsureStatus(DeliveryStatus.OutForDelivery);

        if (ActiveAttempts.Any(a => a.Status == DeliveryAttemptStatus.InProgress))
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' already has an attempt in progress.");
        }

        if (attemptedQuantities.Count == 0)
        {
            throw new DomainException("A delivery attempt must carry at least one lot allocation.");
        }

        foreach (var (allocationId, quantity) in attemptedQuantities)
        {
            var allocation = GetAllocation(allocationId);

            if (Guard.Positive(quantity) > allocation.UndeliveredQuantity)
            {
                throw new DomainException(
                    $"Attempted {quantity} exceeds the {allocation.UndeliveredQuantity} undelivered on the lot allocation.");
            }
        }

        var attemptNumber = _attempts.Count == 0 ? 1 : _attempts.Max(a => a.AttemptNumber) + 1;
        var attempt = new DeliveryAttempt(Id, attemptNumber, attemptedByMemberId, startedAt, attemptedQuantities);
        _attempts.Add(attempt);

        return attempt;
    }

    // deliveredQuantities: lot allocation id → delivered base quantity (missing = 0, i.e. failed).
    public void CompleteAttempt(
        Guid attemptId,
        DateTimeOffset completedAt,
        IReadOnlyDictionary<Guid, long> deliveredQuantities,
        string? receiverName = null,
        string? proofImageUrl = null,
        string? failureReasonCode = null,
        string? note = null)
    {
        EnsureStatus(DeliveryStatus.OutForDelivery);

        var attempt = GetAttempt(attemptId);
        attempt.Complete(completedAt, deliveredQuantities, receiverName, proofImageUrl, failureReasonCode, note);

        foreach (var attemptItem in attempt.ActiveItems.Where(i => i.DeliveredBaseQuantity > 0))
        {
            var allocation = GetAllocation(attemptItem.DeliveryItemLotAllocationId);
            allocation.RecordDelivered(attemptItem.DeliveredBaseQuantity);
            GetItem(allocation.DeliveryItemId).RecordDelivered(attemptItem.DeliveredBaseQuantity);
        }

        if (RemainingBaseQuantity == 0)
        {
            Status = DeliveryStatus.Delivered;
            CompletedAt = completedAt;
        }
        else
        {
            Status = attempt.DeliveredBaseQuantity > 0 ? DeliveryStatus.PartiallyDelivered : DeliveryStatus.RetryPending;
        }
    }

    public void CancelAttempt(Guid attemptId)
    {
        EnsureStatus(DeliveryStatus.OutForDelivery);
        GetAttempt(attemptId).Cancel();
    }

    public void LinkSaleStockMovement(Guid attemptId, Guid stockMovementId) =>
        GetAttempt(attemptId).LinkSaleStockMovement(stockMovementId);

    // Releases/cancels the not-yet-delivered remainder. After a partial delivery the Delivery ends DELIVERED
    // and the cancelled part shows on its items (PARTIALLY_CANCELLED).
    public void Cancel(Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null)
    {
        if (Status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled)
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' is {Status} and cannot be cancelled.");
        }

        if (ActiveAttempts.Any(a => a.Status == DeliveryAttemptStatus.InProgress))
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' has an attempt in progress.");
        }

        foreach (var allocation in ActiveAllocations.Where(a => a.UndeliveredQuantity > 0))
        {
            allocation.ReleaseRemaining(deliveryCancelled: true);
        }

        foreach (var item in ActiveItems)
        {
            item.CancelRemaining();
        }

        var anythingDelivered = ActiveItems.Any(i => i.DeliveredBaseQuantity > 0);

        Status = anythingDelivered ? DeliveryStatus.Delivered : DeliveryStatus.Cancelled;
        CompletedAt = anythingDelivered ? cancelledAt : CompletedAt;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = reason;
    }

    protected override void EnsureCanBeDeleted() => EnsureStatus(DeliveryStatus.Draft);

    private IEnumerable<DeliveryItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private IEnumerable<DeliveryItemLotAllocation> ActiveAllocations => ActiveItems.SelectMany(i => i.ActiveAllocations);

    private IEnumerable<DeliveryAttempt> ActiveAttempts => _attempts.Where(a => !a.IsDeleted);

    private DeliveryItem GetItem(Guid itemId) =>
        ActiveItems.SingleOrDefault(i => i.Id == itemId)
        ?? throw new DomainException($"Item '{itemId}' was not found on delivery '{DeliveryNumber}'.");

    private DeliveryItemLotAllocation GetAllocation(Guid allocationId) =>
        ActiveAllocations.SingleOrDefault(a => a.Id == allocationId)
        ?? throw new DomainException($"Lot allocation '{allocationId}' was not found on delivery '{DeliveryNumber}'.");

    private DeliveryAttempt GetAttempt(Guid attemptId) =>
        ActiveAttempts.SingleOrDefault(a => a.Id == attemptId)
        ?? throw new DomainException($"Attempt '{attemptId}' was not found on delivery '{DeliveryNumber}'.");

    private void EnsureNotOutForDelivery() =>
        EnsureStatus(
            DeliveryStatus.Draft,
            DeliveryStatus.Assigned,
            DeliveryStatus.PartiallyDelivered,
            DeliveryStatus.RetryPending);

    private void EnsureStatus(params DeliveryStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException($"Delivery '{DeliveryNumber}' is {Status}; this action is not allowed.");
        }
    }
}
