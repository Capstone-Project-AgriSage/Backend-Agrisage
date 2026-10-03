using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Payments.Entities;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task B5 registers the real IDebtRepaymentPosting (then delete this file and its registration).
// Debt repayments are refused (422) until debts exist.
public sealed class TemporaryDebtRepaymentPosting : IDebtRepaymentPosting
{
    public Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
        Payment payment,
        IReadOnlyList<RequestedDebtAllocation>? requested,
        Guid? actorId,
        CancellationToken cancellationToken) =>
        throw new BusinessRuleException("Debt repayment is not available yet: debts arrive with task B5.");
}
