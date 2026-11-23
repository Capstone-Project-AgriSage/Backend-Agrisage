using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// The signed-in seller's own figures of a period (REPORTS.md "My sales"): the orders they created, the goods of those
// orders handed over, and the payments they confirmed. Never cost of goods or gross profit. Read only, sums in SQL.
public sealed class MySalesReportService(IAgriSageDbContext db, ReportScope scope, ICurrentUserService user) : IMySalesReportService
{
    public async Task<MySalesReportResponse> GetAsync(MySalesReportRequest request, CancellationToken token)
    {
        // Whoever sells at the counter may see their own figures (COUNTER_SALES.SELL, decision 2026-11-23).
        var store = await scope.GetStoreIdAsync(token, "COUNTER_SALES.SELL");
        var me = user.UserId!.Value; // ReportScope rejects an unauthenticated caller.
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to).AddDays(1);

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.StoreId == store && o.CreatedBy == me && o.CreatedAt >= start && o.CreatedAt < end)
            .GroupBy(o => 1).Select(g => new
            {
                Created = g.Count(),
                Completed = g.Count(o => o.Status == OrderStatus.Completed),
                Cancelled = g.Count(o => o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.PartiallyCancelled)
            }).SingleOrDefaultAsync(token);

        // The /revenue definitions for the orders I created.
        var sales = await RevenueReportService.TotalsAsync(new RevenueLedger(db).Query(store,
            new RevenueFilterRequest { FromDate = from, ToDate = to, StaffUserId = me }), token);

        // As /payments: gross receipts I confirmed. payOS payments confirmed by the webhook have no confirming person.
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.StoreId == store && p.ConfirmedBy == me && p.ConfirmedAt >= start && p.ConfirmedAt < end
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded || p.Status == PaymentStatus.Refunded))
            .GroupBy(p => 1).Select(g => new
            {
                Count = g.Count(),
                Amount = g.Sum(p => p.Amount),
                OrderAmount = g.Sum(p => p.PaymentContext == PaymentContext.OrderPayment ? p.Amount : 0m),
                DebtAmount = g.Sum(p => p.PaymentContext == PaymentContext.DebtRepayment ? p.Amount : 0m)
            }).SingleOrDefaultAsync(token);

        var created = orders?.Created ?? 0;
        var completed = orders?.Completed ?? 0;
        var cancelled = orders?.Cancelled ?? 0;

        return new(from, to,
            new(created, completed, cancelled, created - completed - cancelled),
            new(sales.OrderCount, sales.FulfilledValue, sales.ReturnValue, sales.NetSales, sales.AverageOrderValue),
            new(payments?.Count ?? 0, payments?.Amount ?? 0m, payments?.OrderAmount ?? 0m, payments?.DebtAmount ?? 0m));
    }
}
