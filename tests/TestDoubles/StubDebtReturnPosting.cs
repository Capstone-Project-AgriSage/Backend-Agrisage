using AgriSage.Application.Features.Debt;

namespace AgriSage.Tests.TestDoubles;

// Test-only no-op when the return fixture contains no debt to reduce.
public sealed class StubDebtReturnPosting : IDebtReturnPosting
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
