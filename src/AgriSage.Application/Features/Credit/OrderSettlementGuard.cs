using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Credit;

public sealed class OrderSettlementGuard(IAgriSageDbContext db, IRowLockService locks,
    IOrderPrepaymentLedger prepayments, ICreditEligibilityService eligibility, IDateTimeProvider clock) : IOrderSettlementGuard
{
    public async Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken token)
    {
        var paid = await prepayments.GetPaidAmountAsync(order.Id, token);
        if (order.SettlementType != SettlementType.Credit)
        {
            if (paid < order.TotalAmount) throw new BusinessRuleException("Payment does not cover the order total.");
            return new(null);
        }
        var farmer = order.FarmerProfileId ?? throw new BusinessRuleException("Walk-in customers cannot buy on credit.");
        await locks.LockFarmerProfileAsync(farmer, token);
        var required = Math.Max(0, order.TotalAmount - paid);
        CreditEligibilityService.RequireEligible(await eligibility.CheckAsync(farmer, required, token));
        var profile = await db.FarmerCreditProfiles.Include(p => p.CreditTier)
            .SingleAsync(p => p.StoreId == order.StoreId && p.FarmerProfileId == farmer, token);
        if (await db.CreditReservations.AnyAsync(r => r.OrderId == order.Id
            && r.AmountReserved > r.AmountConsumed + r.AmountReleased, token))
            throw new BusinessRuleException("The order already has a credit reservation.");
        if (required > 0) db.CreditReservations.Add(new CreditReservation(order.StoreId, profile.Id, order.Id, required, actorId, clock.UtcNow));
        return new(profile.CreditTier!.DefaultPaymentTermDays);
    }

    public async Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken token)
    {
        if (order.SettlementType != SettlementType.Credit || order.FarmerProfileId is not { } farmer) return;
        await locks.LockFarmerProfileAsync(farmer, token);
        var reservations = await db.CreditReservations.Where(r => r.OrderId == order.Id && r.StoreId == order.StoreId
            && r.AmountReserved > r.AmountConsumed + r.AmountReleased).ToListAsync(token);
        var terminal = order.Status is OrderStatus.Cancelled or OrderStatus.Completed or OrderStatus.PartiallyCancelled;
        var required = terminal ? 0 : Math.Max(0,
            order.Items.Where(i => !i.IsDeleted).Sum(i => i.RemainingBaseQuantity * i.UnitPrice / i.ConversionToBaseSnapshot)
            - await prepayments.GetAvailableAsync(order.Id, token));
        foreach (var r in reservations)
        {
            var release = Math.Max(0, r.RemainingAmount - required);
            if (release > 0) r.Release(release, actorId, clock.UtcNow, reason);
        }
    }
}
