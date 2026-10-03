namespace AgriSage.Application.Features.Payments;

// Cross-flow interface (docs/reference/api-flows/README.md §4.2). Owner: task F1.3.
// Used by credit and debt (F3.3, F3.4). Runs inside the caller's transaction and never saves.
public interface IOrderPrepaymentLedger
{
    // Σ active ORDER allocations of PAID payments of the order.
    Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken);

    // Σ (allocated − prepayment consumed) of those allocations.
    Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken);

    // Consumes up to maxAmount, oldest allocation first (rule 25); returns the amount consumed.
    Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken);
}
