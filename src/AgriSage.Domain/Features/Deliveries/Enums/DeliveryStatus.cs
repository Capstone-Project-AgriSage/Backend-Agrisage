namespace AgriSage.Domain.Features.Deliveries.Enums;

public enum DeliveryStatus
{
    Draft,
    Assigned,
    OutForDelivery,
    PartiallyDelivered,
    RetryPending,
    Delivered,
    Cancelled
}
