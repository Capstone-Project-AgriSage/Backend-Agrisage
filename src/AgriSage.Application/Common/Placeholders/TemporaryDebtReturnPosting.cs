using AgriSage.Application.Features.Debt;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task B5 registers the real IDebtReturnPosting (then delete this file and its registration).
// No debts exist yet, so no return value reduces debt: the whole return value is refunded.
public sealed class TemporaryDebtReturnPosting : IDebtReturnPosting
{
    public Task<decimal> ApplyReturnAsync(
        Guid orderId,
        Guid salesReturnId,
        decimal returnValue,
        Guid actorId,
        Guid? sourceStockMovementId,
        CancellationToken cancellationToken) =>
        Task.FromResult(0m);
}
