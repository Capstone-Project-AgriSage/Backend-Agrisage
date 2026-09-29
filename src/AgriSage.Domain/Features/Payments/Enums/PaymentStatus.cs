namespace AgriSage.Domain.Features.Payments.Enums;

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
