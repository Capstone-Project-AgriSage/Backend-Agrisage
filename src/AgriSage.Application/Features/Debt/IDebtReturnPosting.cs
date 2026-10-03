namespace AgriSage.Application.Features.Debt;

// Cross-module interface (API_CONTRACT_CUSTOMERS_CREDIT.md §7.3). Owner: group B (task B5). Used by returns (C4).
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
