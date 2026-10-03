using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Application.Features.Payments;

// Cross-module interface (API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md §3.2, API_CONTRACT_SALES.md §9.4).
// Owner: group C (task C1). Used by order cancellation (A2, A4). Runs inside the caller's transaction, never saves.
public interface IOrderPaymentCancellation
{
    // After the order became CANCELLED, or PARTIALLY_CANCELLED with nothing left open: cancel its PENDING payOS
    // payments, reverse the unconsumed ORDER allocation of each PAID payment and request one PENDING refund per
    // payment through Order.RequestCancellationRefund (database design §35.18). Returns the requested refunds.
    Task<IReadOnlyList<CancellationRefundInfo>> ReverseForCancelledOrderAsync(
        Order order, Guid actorId, string reason, CancellationToken cancellationToken);
}

public sealed record CancellationRefundInfo(
    Guid RefundId, string RefundNumber, Guid PaymentId, RefundMethod RefundMethod, decimal Amount);
