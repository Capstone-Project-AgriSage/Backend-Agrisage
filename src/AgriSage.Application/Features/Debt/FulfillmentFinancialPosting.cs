using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// Called after physical stock posting, within the order/delivery owner's transaction. Never saves.
public sealed class FulfillmentFinancialPosting(IAgriSageDbContext db, IRowLockService locks,
    IOrderPrepaymentLedger prepayments, AuditTrail audit) : IFulfillmentFinancialPosting
{
    public async Task PostAsync(FulfillmentPostingContext c, CancellationToken token)
    {
        if (c.Order.Status is not (OrderStatus.Completed or OrderStatus.PartiallyFulfilled or OrderStatus.PartiallyCancelled))
            throw new BusinessRuleException("Debt posting requires successful fulfillment.");
        var value = c.Lines.Sum(l => l.FulfilledValue);
        if (c.Lines.Count == 0 || c.Lines.Any(l => l.FulfilledBaseQuantity <= 0 || l.FulfilledValue < 0))
            throw new BusinessRuleException("Financial posting requires successful fulfillment lines.");
        if (c.Order.FarmerProfileId is { } farmer) await locks.LockFarmerProfileAsync(farmer, token);
        if (await db.StockMovements.AsNoTracking().AnyAsync(m => m.Id == c.StockMovementId && m.StoreId == c.Order.StoreId
            && m.OrderId == c.Order.Id && m.Status == AgriSage.Domain.Features.Inventory.Enums.StockMovementStatus.Posted, token)) return;
        if (db.DebtEntries.Local.Any(e => e.SourceStockMovementId == c.StockMovementId)
            || await db.DebtEntries.IgnoreQueryFilters().AnyAsync(e => e.SourceStockMovementId == c.StockMovementId, token)) return;
        var prepaid = await prepayments.ConsumeAsync(c.Order.Id, value, token);
        var unpaid = value - prepaid;
        if (unpaid == 0) return;
        if (c.Order.SettlementType != SettlementType.Credit || c.Order.FarmerProfileId == null)
            throw new BusinessRuleException("A full-payment fulfillment cannot have unpaid value.");
        var term = c.Order.CreditTermDaysSnapshot ?? throw new BusinessRuleException("The credit order has no payment-term snapshot.");
        var reservation = await db.CreditReservations.SingleOrDefaultAsync(r => r.OrderId == c.Order.Id && r.StoreId == c.Order.StoreId
            && r.AmountReserved > r.AmountConsumed + r.AmountReleased, token)
            ?? db.CreditReservations.Local.SingleOrDefault(r => r.OrderId == c.Order.Id && r.StoreId == c.Order.StoreId && r.RemainingAmount > 0)
            ?? throw new BusinessRuleException("The fulfillment has no remaining credit reservation.");
        var account = await db.DebtAccounts.SingleAsync(a => a.StoreId == c.Order.StoreId && a.FarmerProfileId == c.Order.FarmerProfileId, token);
        var number = await DocumentNumbers.NextAsync(db.DebtEntries.IgnoreQueryFilters().AsNoTracking().Select(e => e.EntryNumber),
            DocumentNumbers.DebtEntry, BusinessCalendar.Today(c.FulfilledAt), token);
        reservation.Consume(unpaid);
        var posting = account.CreateCreditSaleEntry(number,
            c.Source == FulfillmentSource.Pickup ? DebtEntrySourceType.Pickup : DebtEntrySourceType.Delivery,
            c.Order.Id, c.StockMovementId, value, prepaid, BusinessCalendar.Today(c.FulfilledAt).AddDays(term), c.ActorId,
            c.FulfilledAt, c.DeliveryId, c.DeliveryAttemptId);
        db.DebtEntries.Add(posting.Entry);
        db.DebtTransactions.Add(posting.Transaction);
        if (c.Order.Status is OrderStatus.Completed or OrderStatus.PartiallyCancelled && reservation.RemainingAmount > 0)
            reservation.ReleaseRemaining(c.ActorId, c.FulfilledAt, "Fulfillment completed");
        audit.Record("DEBT_CREATED", "DEBT_ENTRY", posting.Entry.Id, c.Order.StoreId,
            newValues: new { posting.Entry.EntryNumber, posting.Entry.OriginalAmount, posting.Entry.DueDate, c.StockMovementId });
    }
}
