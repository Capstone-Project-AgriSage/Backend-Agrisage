using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task F1.3 registers the real IOrderPrepaymentLedger (then delete this file and its registration).
// Pretends a FULL_PAYMENT order is fully paid and a CREDIT order has no prepayment, so F3.3/F3.4 can be built and tested
// before payments exist. Never enable it outside development and tests once F1.3 is merged.
public sealed class TemporaryOrderPrepaymentLedger(IAgriSageDbContext context) : IOrderPrepaymentLedger
{
    public async Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        return order.SettlementType == SettlementType.FullPayment ? order.TotalAmount : 0m;
    }

    public Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken) =>
        GetPaidAmountAsync(orderId, cancellationToken);

    public async Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        return order.SettlementType == SettlementType.FullPayment ? maxAmount : 0m;
    }

    private async Task<(SettlementType SettlementType, decimal TotalAmount)> LoadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.SettlementType, o.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        return (order.SettlementType, order.TotalAmount);
    }
}
