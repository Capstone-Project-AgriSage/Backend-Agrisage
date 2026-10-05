using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

public sealed class DebtReturnPosting(IAgriSageDbContext db, IRowLockService locks,
    IDateTimeProvider clock, AuditTrail audit) : IDebtReturnPosting
{
    public async Task<decimal> ApplyReturnAsync(Guid orderId, Guid salesReturnId, decimal returnValue,
        Guid actorId, Guid? sourceStockMovementId, CancellationToken token)
    {
        if (!Credit.CreditMoney.Valid(returnValue)) throw new BusinessRuleException("Return value must be non-negative money with at most two decimal places.");
        var store = await ActiveStore.GetIdAsync(db, token);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.StoreId == store, token)
            ?? throw new NotFoundException("Order", orderId);
        var salesReturn = await db.SalesReturns.SingleOrDefaultAsync(r => r.Id == salesReturnId && r.OrderId == orderId && r.StoreId == store, token)
            ?? throw new BusinessRuleException("Return must belong to this order and store.");
        if (salesReturn.Status is not (SalesReturnStatus.Received or SalesReturnStatus.Inspected or SalesReturnStatus.PartiallyResolved or SalesReturnStatus.Completed)
            || returnValue > salesReturn.TotalReturnAmount)
            throw new BusinessRuleException("Debt adjustment requires a received return and cannot exceed its value.");
        if (sourceStockMovementId is { } source && !await db.StockMovements.AnyAsync(m => m.Id == source && m.OrderId == orderId && m.StoreId == store, token))
            throw new BusinessRuleException("The fulfillment source must belong to this return's order.");
        if (order.FarmerProfileId is not { } farmer || returnValue == 0) return 0;
        await locks.LockFarmerProfileAsync(farmer, token);
        var previous = await db.DebtTransactions.AsNoTracking().Where(t => t.SalesReturnId == salesReturnId
            && t.TransactionType == DebtTransactionType.Return).SumAsync(t => (decimal?)-t.AmountDelta, token) ?? 0;
        previous += db.DebtTransactions.Local.Where(t => t.SalesReturnId == salesReturnId
            && t.TransactionType == DebtTransactionType.Return && db.DebtTransactions.Entry(t).State == EntityState.Added).Sum(t => -t.AmountDelta);
        if (previous > 0) return previous;
        var account = await db.DebtAccounts.SingleOrDefaultAsync(a => a.StoreId == store && a.FarmerProfileId == farmer, token);
        if (account == null) return 0;
        var entries = await db.DebtEntries.Where(e => e.DebtAccountId == account.Id && e.OrderId == orderId
            && e.OutstandingAmount > 0 && e.Status != DebtEntryStatus.Cancelled && e.Status != DebtEntryStatus.Paid)
            .OrderByDescending(e => sourceStockMovementId != null && e.SourceStockMovementId == sourceStockMovementId)
            .ThenBy(e => e.DueDate).ThenBy(e => e.Id).ToListAsync(token);
        var left = returnValue;
        foreach (var entry in entries)
        {
            if (left == 0) break;
            var amount = Math.Min(left, entry.OutstandingAmount);
            db.DebtTransactions.Add(account.ApplyReturn(entry, amount, salesReturnId, clock.UtcNow, actorId));
            left -= amount;
        }
        audit.Record("DEBT_RETURN_APPLIED", "SALES_RETURN", salesReturnId, store,
            newValues: new { OrderId = orderId, AppliedAmount = returnValue - left });
        return returnValue - left;
    }
}
