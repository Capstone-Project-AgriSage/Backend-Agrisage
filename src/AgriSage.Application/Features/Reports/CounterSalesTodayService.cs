using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// Read-only daily counter metrics. Fulfillment, receipt and debt origination have separate event dates.
public sealed class CounterSalesTodayService(IAgriSageDbContext db, ReportScope scope, IDateTimeProvider clock)
    : ICounterSalesTodayService
{
    public async Task<CounterSalesTodayResponse> GetAsync(CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var start = BusinessCalendar.StartOfDay(today);
        var end = start.AddDays(1);
        var orders = db.Orders.AsNoTracking().Where(o => o.StoreId == store && o.Source == OrderSource.Counter);
        var payments = db.Payments.AsNoTracking().Where(p => p.StoreId == store && p.ConfirmedAt <= now
            && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded || p.Status == PaymentStatus.Refunded));
        var allocations = db.PaymentAllocations.AsNoTracking().Where(a => a.Status == PaymentAllocationStatus.Active);

        var orderAllocations = from a in allocations
            join p in payments on a.PaymentId equals p.Id
            where a.AllocationType == PaymentAllocationType.Order && a.OrderId != null && a.AllocatedAt <= now
            select new { OrderId = a.OrderId!.Value, Amount = a.AllocatedAmount, p.ConfirmedAt };
        var debtAllocations = from a in allocations
            join p in payments on a.PaymentId equals p.Id
            join e in db.DebtEntries.AsNoTracking() on a.DebtEntryId equals e.Id
            join account in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals account.Id
            where a.AllocationType == PaymentAllocationType.Debt && e.OrderId != null
                && account.StoreId == store && a.AllocatedAt <= now
            select new { OrderId = e.OrderId!.Value, Amount = a.AllocatedAmount, p.ConfirmedAt };
        var paidAllocations = orderAllocations.Concat(debtAllocations);
        var fullyPaidOrders = orders.Where(o => o.TotalAmount > 0
            && (paidAllocations.Where(a => a.OrderId == o.Id).Sum(a => (decimal?)a.Amount) ?? 0m) >= o.TotalAmount);

        // Reuse the authoritative SALE movement valuation, including partial handovers and price overrides.
        var revenue = await new RevenueLedger(db).Query(store, new RevenueFilterRequest
            { FromDate = today, ToDate = today, Source = "COUNTER" })
            .Where(e => e.IsSale && e.At <= now && fullyPaidOrders.Any(o => o.Id == e.OrderId))
            .SumAsync(e => (decimal?)e.Value, token) ?? 0m;
        var completed = await orders.CountAsync(o => o.Status == OrderStatus.Completed
            && o.CompletedAt >= start && o.CompletedAt < end && o.CompletedAt <= now, token);

        // Order receipts count once per payment; debt receipts count only allocations to counter orders.
        var orderReceipts = await payments.Where(p => p.PaymentContext == PaymentContext.OrderPayment
                && p.ConfirmedAt >= start && p.ConfirmedAt < end && orders.Any(o => o.Id == p.OrderId))
            .SumAsync(p => (decimal?)p.Amount, token) ?? 0m;
        var debtReceipts = await debtAllocations.Where(a => a.ConfirmedAt >= start && a.ConfirmedAt < end
                && orders.Any(o => o.Id == a.OrderId))
            .SumAsync(a => (decimal?)a.Amount, token) ?? 0m;
        var creditSales = await (from t in db.DebtTransactions.AsNoTracking()
            join e in db.DebtEntries.AsNoTracking() on t.DebtEntryId equals e.Id
            join account in db.DebtAccounts.AsNoTracking() on t.DebtAccountId equals account.Id
            join o in orders on e.OrderId equals o.Id
            where account.StoreId == store && e.DebtAccountId == t.DebtAccountId
                && o.SettlementType == SettlementType.Credit && t.Status == DebtTransactionStatus.Posted
                && t.TransactionType == DebtTransactionType.CreditSale && t.AmountDelta > 0
                && t.OccurredAt >= start && t.OccurredAt < end && t.OccurredAt <= now
            select (decimal?)t.AmountDelta).SumAsync(token) ?? 0m;

        return new(today, now, revenue, completed, orderReceipts + debtReceipts, creditSales);
    }
}
