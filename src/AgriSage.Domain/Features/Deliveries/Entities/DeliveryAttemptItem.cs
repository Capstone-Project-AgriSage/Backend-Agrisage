using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// Quantity of one lot allocation carried by one attempt. Changed only through Delivery.
// Invariant: delivered + failed = attempted once the attempt is completed.
public sealed class DeliveryAttemptItem : SoftDeletableChildEntity
{
    private DeliveryAttemptItem()
    {
    }

    internal DeliveryAttemptItem(Guid deliveryAttemptId, Guid deliveryItemLotAllocationId, long attemptedBaseQuantity)
    {
        DeliveryAttemptId = deliveryAttemptId;
        DeliveryItemLotAllocationId = deliveryItemLotAllocationId;
        AttemptedBaseQuantity = Guard.Positive(attemptedBaseQuantity);
    }

    public Guid DeliveryAttemptId { get; private set; }

    public Guid DeliveryItemLotAllocationId { get; private set; }

    public long AttemptedBaseQuantity { get; private set; }

    public long DeliveredBaseQuantity { get; private set; }

    public long FailedBaseQuantity { get; private set; }

    public string? Note { get; private set; }

    internal void RecordOutcome(long deliveredBaseQuantity)
    {
        if (deliveredBaseQuantity < 0 || deliveredBaseQuantity > AttemptedBaseQuantity)
        {
            throw new DomainException(
                $"Delivered quantity {deliveredBaseQuantity} must be between 0 and the attempted quantity {AttemptedBaseQuantity}.");
        }

        DeliveredBaseQuantity = deliveredBaseQuantity;
        FailedBaseQuantity = AttemptedBaseQuantity - deliveredBaseQuantity;
    }
}
