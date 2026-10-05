using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Payments.Entities;

namespace AgriSage.Tests.TestDoubles;

// Test-only refusal when the payment fixture deliberately contains no debts.
public sealed class StubDebtRepaymentPosting : IDebtRepaymentPosting
{
    public Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
        Payment payment,
        IReadOnlyList<RequestedDebtAllocation>? requested,
        Guid? actorId,
        CancellationToken cancellationToken) =>
        throw new BusinessRuleException("This isolated payment test does not contain debts.");
}
