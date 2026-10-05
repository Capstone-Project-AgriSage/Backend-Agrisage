using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

// Real IOrderPrepaymentLedger (task F1.3, FLOW_1 §5). Runs inside the caller's unit of work and never saves.
// It reads the order's payments from the database and also the payments still unsaved in the same unit of work
// (DbSet.Local, api-flows README §3.2), so a payment created a moment ago by F1.7 is already counted. Status and
// allocations are read from the tracked objects, so a payment marked PAID in this unit of work counts too.
public sealed class OrderPrepaymentLedger(IAgriSageDbContext context) : IOrderPrepaymentLedger
{
    public async Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken) =>
        (await ActiveAllocationsAsync(orderId, cancellationToken)).Sum(x => x.Allocation.AllocatedAmount);

    public async Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken) =>
        (await ActiveAllocationsAsync(orderId, cancellationToken)).Sum(x => x.Allocation.AvailablePrepayment);

    // Oldest allocation first (business rule 25).
    public async Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken)
    {
        var remaining = maxAmount;
        var consumed = 0m;

        foreach (var (payment, allocation) in await ActiveAllocationsAsync(orderId, cancellationToken))
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, allocation.AvailablePrepayment);
            if (take <= 0)
            {
                continue;
            }

            payment.ConsumePrepayment(allocation.Id, take);
            remaining -= take;
            consumed += take;
        }

        return consumed;
    }

    private async Task<List<(Payment Payment, PaymentAllocation Allocation)>> ActiveAllocationsAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var payments = await context.Payments.Include(p => p.Allocations)
            .Where(p => p.OrderId == orderId)
            .ToListAsync(cancellationToken);

        var known = payments.Select(p => p.Id).ToHashSet();
        payments.AddRange(context.Payments.Local.Where(p => p.OrderId == orderId && !p.IsDeleted && known.Add(p.Id)));

        return payments
            .Where(p => !p.IsDeleted && p.Status is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            .SelectMany(p => p.Allocations
                .Where(a => !a.IsDeleted && a.IsActive && a.AllocationType == PaymentAllocationType.Order && a.OrderId == orderId)
                .Select(a => (Payment: p, Allocation: a)))
            .OrderBy(x => x.Allocation.AllocatedAt).ThenBy(x => x.Allocation.Id)
            .ToList();
    }
}
