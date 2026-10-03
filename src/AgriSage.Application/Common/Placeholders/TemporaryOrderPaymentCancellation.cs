using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Entities;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task F1.6 registers the real IOrderPaymentCancellation (then delete this file and its registration).
// No payments exist yet, so there is nothing to reverse or refund.
public sealed class TemporaryOrderPaymentCancellation : IOrderPaymentCancellation
{
    public Task<IReadOnlyList<CancellationRefundInfo>> ReverseForCancelledOrderAsync(
        Order order, Guid actorId, string reason, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CancellationRefundInfo>>([]);
}
