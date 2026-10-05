using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;

namespace AgriSage.Tests.TestDoubles;

// Test-only isolation for inventory/order tests: funding is outside their scope; credit is refused.
public sealed class StubOrderSettlementGuard : IOrderSettlementGuard
{
    public Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken cancellationToken) =>
        order.SettlementType == SettlementType.Credit
            ? throw new BusinessRuleException("This isolated order test does not support credit.")
            : Task.FromResult(new SettlementResult(null));

    public Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
