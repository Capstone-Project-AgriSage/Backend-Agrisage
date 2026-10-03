using AgriSage.Domain.Features.Orders.Entities;

namespace AgriSage.Application.Features.Credit;

// Cross-flow interface (docs/reference/api-flows/README.md §4.4). Owner: task F3.3.
// Used by order confirmation and cancellation (F1.4, F1.6, F1.7). Runs inside the caller's transaction and never saves.
public interface IOrderSettlementGuard
{
    // Before any stock is reserved. FULL_PAYMENT: PAID order payments must cover the total (decision D3).
    // CREDIT: ACTIVE credit profile, required credit = total − paid ≤ available credit, credit reservation created.
    // Throws BusinessRuleException when the order cannot be confirmed.
    Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken cancellationToken);

    // The order (or its remainder) is cancelled: release the unused credit reservation.
    Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken cancellationToken);
}

// CreditTermDays is set for CREDIT orders only (snapshotted on the order at confirmation).
public sealed record SettlementResult(int? CreditTermDays);
