namespace AgriSage.Domain.Features.Orders.Enums;

public enum OrderStatus
{
    PendingConfirmation,
    Confirmed,
    Preparing,
    ReadyForFulfillment,
    PartiallyFulfilled,
    Completed,
    Cancelled,
    PartiallyCancelled
}
