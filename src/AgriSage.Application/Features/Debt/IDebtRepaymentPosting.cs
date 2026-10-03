using AgriSage.Domain.Features.Payments.Entities;

namespace AgriSage.Application.Features.Debt;

// Cross-module interface (API_CONTRACT_CUSTOMERS_CREDIT.md §7.2). Owner: group B (task B5). Used by payments (C1, C3).
// Runs inside the caller's transaction and never saves.
public interface IDebtRepaymentPosting
{
    // A DEBT_REPAYMENT payment became PAID: creates the DEBT payment allocations (explicit list, or oldest due date
    // first when null) and one PAYMENT debt transaction per allocation.
    Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
        Payment payment,
        IReadOnlyList<RequestedDebtAllocation>? requested,
        Guid? actorId,
        CancellationToken cancellationToken);
}

public sealed record RequestedDebtAllocation(Guid DebtEntryId, decimal Amount);

public sealed record DebtAllocationResult(Guid DebtEntryId, Guid PaymentAllocationId, decimal Amount);
