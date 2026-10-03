using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task B4 registers the real IOrderSettlementGuard (then delete this file and its registration).
// FULL_PAYMENT orders pass without the payment check of decision D3; CREDIT orders are refused (422).
public sealed class TemporaryOrderSettlementGuard : IOrderSettlementGuard
{
    public Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken cancellationToken) =>
        order.SettlementType == SettlementType.Credit
            ? throw new BusinessRuleException("Credit sales are not available yet: credit arrives with task B4.")
            : Task.FromResult(new SettlementResult(null));

    public Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
