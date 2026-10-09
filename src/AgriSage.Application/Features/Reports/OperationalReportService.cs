using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

public sealed class OperationalReportService(IAgriSageDbContext db, ReportScope scope, IDateTimeProvider clock) : IOperationalReportService
{
    private static string By(string? input, string fallback = "DAY") => input?.Trim().ToUpperInvariant() ?? fallback;
    private static (DateTimeOffset Start, DateTimeOffset End) Bounds(ReportPeriodRequest request) =>
        (BusinessCalendar.StartOfDay(request.FromDate!.Value), BusinessCalendar.StartOfDay(request.ToDate!.Value).AddDays(1));

    public async Task<OrderReportResponse> OrdersAsync(OrderReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var (start, end) = Bounds(request);
        var by = By(request.GroupBy, "STATUS");
        var data = await db.Orders.AsNoTracking().Where(o => o.StoreId == store && o.CreatedAt >= start && o.CreatedAt < end)
            .GroupBy(o => new { Status = by == "STATUS" ? o.Status : OrderStatus.PendingConfirmation,
                Source = by == "SOURCE" ? o.Source : OrderSource.Counter,
                Settlement = by == "SETTLEMENT" ? o.SettlementType : SettlementType.FullPayment })
            .Select(g => new { g.Key.Status, g.Key.Source, g.Key.Settlement, Count = g.Count(), Amount = g.Sum(o => o.TotalAmount) })
            .ToListAsync(token);
        var rows = data.Select(g =>
        {
            var key = by == "STATUS" ? EnumText.Format(g.Status) : by == "SOURCE" ? EnumText.Format(g.Source) : EnumText.Format(g.Settlement);
            return new OrderReportRow(key, key, g.Count, g.Amount);
        }).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        return new(request.FromDate!.Value, request.ToDate!.Value, by, rows, new(rows.Sum(r => r.OrderCount), rows.Sum(r => r.OrderValue)));
    }

    public async Task<PaymentReportResponse> PaymentsAsync(PaymentReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var (start, end) = Bounds(request);
        var by = By(request.GroupBy);
        var data = await db.Payments.AsNoTracking().Where(p => p.StoreId == store && p.ConfirmedAt >= start && p.ConfirmedAt < end
                && (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.PartiallyRefunded || p.Status == PaymentStatus.Refunded))
            .GroupBy(p => new { Day = by == "DAY" ? DateOnly.FromDateTime(p.ConfirmedAt!.Value.UtcDateTime.AddHours(7)) : DateOnly.MinValue,
                Method = by == "METHOD" ? p.PaymentMethod : PaymentMethod.Cash,
                Context = by == "CONTEXT" ? p.PaymentContext : PaymentContext.OrderPayment,
                Staff = by == "STAFF" ? p.ConfirmedBy : null })
            .Select(g => new { g.Key.Day, g.Key.Method, g.Key.Context, g.Key.Staff, Count = g.Count(), Amount = g.Sum(p => p.Amount),
                OrderAmount = g.Sum(p => p.PaymentContext == PaymentContext.OrderPayment ? p.Amount : 0m),
                DebtAmount = g.Sum(p => p.PaymentContext == PaymentContext.DebtRepayment ? p.Amount : 0m) }).ToListAsync(token);
        var ids = data.Where(r => r.Staff != null).Select(r => r.Staff!.Value).ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, token);
        var rows = data.Select(g =>
        {
            var key = by switch { "METHOD" => EnumText.Format(g.Method), "CONTEXT" => EnumText.Format(g.Context),
                "STAFF" => g.Staff?.ToString() ?? "SYSTEM", _ => g.Day.ToString("yyyy-MM-dd") };
            return new PaymentReportRow(key, by == "STAFF" && g.Staff is { } id ? names.GetValueOrDefault(id, key) : key, g.Count, g.Amount);
        }).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        var refunded = await db.Refunds.AsNoTracking().Where(r => r.StoreId == store && r.Status == RefundStatus.Completed
            && r.CompletedAt >= start && r.CompletedAt < end).SumAsync(r => r.Amount, token);
        var received = rows.Sum(r => r.ReceivedAmount);
        return new(request.FromDate!.Value, request.ToDate!.Value, by, rows, new(rows.Sum(r => r.PaymentCount), received,
            data.Sum(r => r.OrderAmount), data.Sum(r => r.DebtAmount), refunded, received - refunded));
    }

    public async Task<PurchaseReportResponse> PurchasesAsync(PurchaseReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var (start, end) = Bounds(request);
        var by = By(request.GroupBy);
        var data = await db.GoodsReceipts.AsNoTracking().Where(r => r.StoreId == store && r.Status == GoodsReceiptStatus.Confirmed
                && r.ConfirmedAt >= start && r.ConfirmedAt < end)
            .GroupBy(r => new { Day = by == "DAY" ? DateOnly.FromDateTime(r.ConfirmedAt!.Value.UtcDateTime.AddHours(7)) : DateOnly.MinValue,
                Supplier = by == "SUPPLIER" ? (Guid?)r.SupplierId : null })
            .Select(g => new { g.Key.Day, g.Key.Supplier, Count = g.Count(), Amount = g.Sum(r => r.TotalAmount) }).ToListAsync(token);
        var ids = data.Where(r => r.Supplier != null).Select(r => r.Supplier!.Value).ToList();
        var names = await db.Suppliers.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, token);
        var rows = data.Select(g => new PurchaseReportRow(g.Supplier?.ToString() ?? g.Day.ToString("yyyy-MM-dd"),
            g.Supplier is { } id ? names.GetValueOrDefault(id, id.ToString()) : g.Day.ToString("yyyy-MM-dd"), g.Count, g.Amount))
            .OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        return new(request.FromDate!.Value, request.ToDate!.Value, by, rows, new(rows.Sum(r => r.ReceiptCount), rows.Sum(r => r.PurchaseAmount)));
    }

    public async Task<ReturnReportResponse> ReturnsAsync(ReturnReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var (start, end) = Bounds(request);
        var by = By(request.GroupBy);
        var data = await db.SalesReturns.AsNoTracking().Where(r => r.StoreId == store && r.Status == SalesReturnStatus.Completed
                && r.CompletedAt >= start && r.CompletedAt < end)
            .GroupBy(r => new { Day = by == "DAY" ? DateOnly.FromDateTime(r.CompletedAt!.Value.UtcDateTime.AddHours(7)) : DateOnly.MinValue,
                Customer = by == "CUSTOMER" ? r.FarmerProfileId : null })
            .Select(g => new { g.Key.Day, g.Key.Customer, Count = g.Count(), Amount = g.Sum(r => r.TotalReturnAmount),
                Debt = g.Sum(r => r.TotalDebtAdjustment), Refund = g.Sum(r => r.TotalRefundAmount) }).ToListAsync(token);
        var ids = data.Where(r => r.Customer != null).Select(r => r.Customer!.Value).ToList();
        var names = await db.FarmerProfiles.IgnoreQueryFilters().AsNoTracking().Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.User.FullName }).ToDictionaryAsync(f => f.Id, f => f.FullName, token);
        var rows = data.Select(g =>
        {
            var key = by == "CUSTOMER" ? g.Customer?.ToString() ?? "WALK_IN" : g.Day.ToString("yyyy-MM-dd");
            return new ReturnReportRow(key, by == "CUSTOMER" ? g.Customer is { } id ? names.GetValueOrDefault(id, key) : "Khách lẻ" : key,
                g.Count, g.Amount, g.Debt, g.Refund);
        }).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        return new(request.FromDate!.Value, request.ToDate!.Value, by, rows,
            new(rows.Sum(r => r.ReturnCount), rows.Sum(r => r.ReturnAmount), rows.Sum(r => r.DebtAdjustmentAmount), rows.Sum(r => r.RefundAmount)));
    }

    public async Task<RefundReportResponse> RefundsAsync(RefundReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var (start, end) = Bounds(request);
        var by = By(request.GroupBy);
        var data = await db.Refunds.AsNoTracking().Where(r => r.StoreId == store && r.Status == RefundStatus.Completed
                && r.CompletedAt >= start && r.CompletedAt < end)
            .GroupBy(r => new { Day = by == "DAY" ? DateOnly.FromDateTime(r.CompletedAt!.Value.UtcDateTime.AddHours(7)) : DateOnly.MinValue,
                Method = by == "METHOD" ? r.RefundMethod : RefundMethod.Cash,
                IsReturn = by == "SOURCE" && r.SalesReturnId != null })
            .Select(g => new { g.Key.Day, g.Key.Method, g.Key.IsReturn, Count = g.Count(), Amount = g.Sum(r => r.Amount) }).ToListAsync(token);
        var rows = data.Select(g =>
        {
            var key = by == "METHOD" ? EnumText.Format(g.Method) : by == "SOURCE" ? g.IsReturn ? "SALES_RETURN" : "ORDER" : g.Day.ToString("yyyy-MM-dd");
            return new RefundReportRow(key, key, g.Count, g.Amount);
        }).OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        return new(request.FromDate!.Value, request.ToDate!.Value, by, rows, new(rows.Sum(r => r.RefundCount), rows.Sum(r => r.RefundedAmount)));
    }

    public async Task<CreditExposureReportResponse> CreditAsync(CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var at = clock.UtcNow;
        var data = await db.FarmerCreditProfiles.AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => new { p.FarmerProfileId, p.FarmerProfile.User.FullName, p.Status, p.CreditLimit,
                Outstanding = db.DebtAccounts.Where(a => a.StoreId == store && a.FarmerProfileId == p.FarmerProfileId).Sum(a => (decimal?)a.CurrentBalance) ?? 0m,
                Reserved = db.CreditReservations.Where(r => r.StoreId == store && r.FarmerCreditProfileId == p.Id
                    && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed))
                    .Sum(r => (decimal?)(r.AmountReserved - r.AmountConsumed - r.AmountReleased)) ?? 0m })
            .OrderByDescending(p => p.Outstanding + p.Reserved).ThenBy(p => p.FarmerProfileId).ToListAsync(token);
        var rows = data.Select(p => new CreditExposureRow(p.FarmerProfileId, p.FullName, EnumText.Format(p.Status), p.CreditLimit,
            p.Outstanding, p.Reserved, p.Outstanding + p.Reserved,
            p.Status == FarmerCreditProfileStatus.Active ? Math.Max(p.CreditLimit - p.Outstanding - p.Reserved, 0m) : 0m,
            p.CreditLimit == 0 ? null : CostRounding.RoundMoney((p.Outstanding + p.Reserved) * 100m / p.CreditLimit))).ToList();
        return new(at, rows, new(rows.Sum(r => r.CreditLimit), rows.Sum(r => r.Outstanding), rows.Sum(r => r.ReservedCredit), rows.Sum(r => r.Exposure), rows.Sum(r => r.AvailableCredit)));
    }
}
