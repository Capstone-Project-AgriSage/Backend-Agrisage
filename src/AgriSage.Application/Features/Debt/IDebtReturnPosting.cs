namespace AgriSage.Application.Features.Debt;

// Cross-flow interface (docs/reference/api-flows/README.md §4.8). Owner: task F3.5. Used by returns (F4.4).
// Runs inside the caller's transaction and never saves.
public interface IDebtReturnPosting
{
    // Reduces the unpaid debt attributable to the returned goods first (rule 32) with RETURN debt transactions linked
    // to the sales return; returns the amount applied, the caller refunds the rest.
    Task<decimal> ApplyReturnAsync(
        Guid orderId,
        Guid salesReturnId,
        decimal returnValue,
        Guid actorId,
        Guid? sourceStockMovementId,
        CancellationToken cancellationToken);
}
