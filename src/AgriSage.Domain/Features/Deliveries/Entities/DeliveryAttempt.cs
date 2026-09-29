using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// One actual delivery attempt. Changed only through Delivery.
// SUCCESS / PARTIAL_SUCCESS with delivered goods requires a proof image.
public sealed class DeliveryAttempt : SoftDeletableChildEntity
{
    private readonly List<DeliveryAttemptItem> _items = [];

    private DeliveryAttempt()
    {
    }

    internal DeliveryAttempt(
        Guid deliveryId,
        int attemptNumber,
        Guid attemptedByMemberId,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<Guid, long> attemptedQuantities)
    {
        DeliveryId = deliveryId;
        AttemptNumber = attemptNumber;
        AttemptedByMemberId = attemptedByMemberId;
        StartedAt = startedAt;
        Status = DeliveryAttemptStatus.InProgress;

        foreach (var (allocationId, quantity) in attemptedQuantities)
        {
            _items.Add(new DeliveryAttemptItem(Id, allocationId, quantity));
        }
    }

    public Guid DeliveryId { get; private set; }

    public int AttemptNumber { get; private set; }

    public Guid AttemptedByMemberId { get; private set; }

    public DeliveryAttemptStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    // Failure reason code values are not enumerated by the database design.
    public string? FailureReasonCode { get; private set; }

    public string? Note { get; private set; }

    public string? ReceiverName { get; private set; }

    public string? ProofImageUrl { get; private set; }

    public Guid? SaleStockMovementId { get; private set; }

    public IReadOnlyCollection<DeliveryAttemptItem> Items => _items.AsReadOnly();

    public long DeliveredBaseQuantity => ActiveItems.Sum(i => i.DeliveredBaseQuantity);

    internal IEnumerable<DeliveryAttemptItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    internal void Complete(
        DateTimeOffset completedAt,
        IReadOnlyDictionary<Guid, long> deliveredQuantities,
        string? receiverName,
        string? proofImageUrl,
        string? failureReasonCode,
        string? note)
    {
        EnsureInProgress();

        var unknown = deliveredQuantities.Keys.Except(ActiveItems.Select(i => i.DeliveryItemLotAllocationId));

        if (unknown.Any())
        {
            throw new DomainException("Delivered quantities reference lot allocations that are not part of this attempt.");
        }

        // Validate everything before changing any item.
        foreach (var item in ActiveItems)
        {
            var quantity = deliveredQuantities.GetValueOrDefault(item.DeliveryItemLotAllocationId);

            if (quantity < 0 || quantity > item.AttemptedBaseQuantity)
            {
                throw new DomainException(
                    $"Delivered quantity {quantity} must be between 0 and the attempted quantity {item.AttemptedBaseQuantity}.");
            }
        }

        var delivered = deliveredQuantities.Values.Sum();
        var attempted = ActiveItems.Sum(i => i.AttemptedBaseQuantity);

        if (delivered > 0 && string.IsNullOrWhiteSpace(proofImageUrl))
        {
            throw new DomainException("A successful delivery requires a proof image.");
        }

        foreach (var item in ActiveItems)
        {
            item.RecordOutcome(deliveredQuantities.GetValueOrDefault(item.DeliveryItemLotAllocationId));
        }

        Status = delivered == 0 ? DeliveryAttemptStatus.Failed
            : delivered == attempted ? DeliveryAttemptStatus.Success
            : DeliveryAttemptStatus.PartialSuccess;
        CompletedAt = completedAt;
        ReceiverName = receiverName;
        ProofImageUrl = proofImageUrl;
        FailureReasonCode = failureReasonCode;
        Note = note;
    }

    internal void Cancel()
    {
        EnsureInProgress();
        Status = DeliveryAttemptStatus.Cancelled;
    }

    // The SALE Stock Movement that records the Lots physically issued by this attempt.
    internal void LinkSaleStockMovement(Guid stockMovementId)
    {
        if (Status is not (DeliveryAttemptStatus.Success or DeliveryAttemptStatus.PartialSuccess))
        {
            throw new DomainException("Only an attempt that delivered goods has a SALE stock movement.");
        }

        if (SaleStockMovementId is not null)
        {
            throw new DomainException("This attempt is already linked to a SALE stock movement.");
        }

        SaleStockMovementId = stockMovementId;
    }

    private void EnsureInProgress()
    {
        if (Status != DeliveryAttemptStatus.InProgress)
        {
            throw new DomainException($"Delivery attempt #{AttemptNumber} is {Status}.");
        }
    }
}
