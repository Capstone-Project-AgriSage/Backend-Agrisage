using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Credit;

public sealed class CreditReservationAdjuster(IAgriSageDbContext db, IRowLockService locks,
    IDateTimeProvider clock) : ICreditReservationAdjuster
{
    public async Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken token)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, token);
        if (order.FarmerProfileId is not { } farmer) return;
        await locks.LockFarmerProfileAsync(farmer, token);
        var reservation = await db.CreditReservations.FirstOrDefaultAsync(r => r.StoreId == order.StoreId && r.OrderId == orderId
            && r.AmountReserved > r.AmountConsumed + r.AmountReleased, token);
        if (reservation != null && paidAmount > 0)
            reservation.Release(Math.Min(paidAmount, reservation.RemainingAmount), actorId, clock.UtcNow, "Confirmed order prepayment");
    }
}
