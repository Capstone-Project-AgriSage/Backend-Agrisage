using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Shared step (api-flows README §3.2 / §4.9; task F1.6, FLOW_1 §8), also used by the Farmer's own cancel (F2.3) after
// its ownership check. On a tracked order with its items loaded, inside the caller's transaction (the order is locked);
// it never saves:
//   order.Cancel → give the stock reservation back → IOrderSettlementGuard.ReleaseAsync (credit reservation) →
//   IOrderPaymentCancellation (cancel pending payments, give prepayment back, request the refunds).
// Only before anything was fulfilled (PENDING_CONFIRMATION … READY_FOR_FULFILLMENT); afterwards the rest of a line is
// cancelled with cancel-remaining.
public sealed class OrderCanceller(
    IAgriSageDbContext context,
    IRowLockService locks,
    IOrderSettlementGuard settlementGuard,
    IOrderPaymentCancellation paymentCancellation)
{
    public async Task<IReadOnlyList<CancellationRefundInfo>> CancelAsync(
        Order order, Guid actorId, DateTimeOffset at, string reason, CancellationToken cancellationToken)
    {
        order.Cancel(actorId, at, reason);

        await ReleaseReservationAsync(order, actorId, at, reason, cancellationToken);
        await settlementGuard.ReleaseAsync(order, actorId, reason, cancellationToken);

        return await paymentCancellation.ReverseForCancelledOrderAsync(order, actorId, reason, cancellationToken);
    }

    private async Task ReleaseReservationAsync(Order order, Guid actorId, DateTimeOffset at, string reason, CancellationToken cancellationToken)
    {
        var reservation = await context.InventoryReservations.Include(r => r.Items)
            .FirstOrDefaultAsync(
                r => r.OrderId == order.Id
                    && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed),
                cancellationToken);
        if (reservation is null)
        {
            return; // a PENDING order holds no stock
        }

        var open = reservation.Items.Where(i => !i.IsDeleted && i.RemainingQuantity > 0).ToList();
        var lotIds = open.Select(i => i.InventoryLotId).Distinct().ToList();
        await locks.LockLotBalancesAsync(lotIds, cancellationToken);
        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        foreach (var line in open)
        {
            lots[line.InventoryLotId].ReleaseReservation(line.RemainingQuantity);
        }

        reservation.Cancel(actorId, at, reason);
    }
}
