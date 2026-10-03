using AgriSage.Domain.Features.Orders.Entities;

namespace AgriSage.Application.Features.Debt;

// Cross-flow interface (docs/reference/api-flows/README.md §4.6). Owner: task F3.4.
// Used by pickup and delivery fulfillment (F1.5, F2.6) inside their transaction, after the stock is posted; never saves.
public interface IFulfillmentFinancialPosting
{
    // Applies available order prepayment (oldest first), consumes the credit reservation for the unpaid part and
    // creates the Debt Entry + CREDIT_SALE Debt Transaction when unpaid > 0 (due date = fulfillment day + term).
    Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken);
}

public enum FulfillmentSource
{
    Pickup,
    Delivery
}

public sealed record FulfillmentPostingContext(
    Order Order,
    IReadOnlyList<FulfilledLine> Lines,
    FulfillmentSource Source,
    Guid? DeliveryId,
    Guid? DeliveryAttemptId,
    Guid StockMovementId,
    Guid ActorId,
    DateTimeOffset FulfilledAt);

// FulfilledValue = round2(fulfilled base quantity × unit price ÷ conversion), computed by the caller per line.
public sealed record FulfilledLine(Guid OrderItemId, long FulfilledBaseQuantity, decimal FulfilledValue);
