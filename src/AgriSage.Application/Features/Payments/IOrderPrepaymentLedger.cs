namespace AgriSage.Application.Features.Payments;

// Cross-module interface (API_CONTRACT_CUSTOMERS_CREDIT.md §8, API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md §3.1).
// Owner: group C (task C1). Used by credit and debt (B4, B5). Runs inside the caller's transaction and never saves.
public interface IOrderPrepaymentLedger
{
    // Σ active ORDER allocations of PAID payments of the order.
    Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken);

    // Σ (allocated − prepayment consumed) of those allocations.
    Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken);

    // Consumes up to maxAmount, oldest allocation first (rule 25); returns the amount consumed.
    Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken);
}
