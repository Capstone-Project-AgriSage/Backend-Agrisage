namespace AgriSage.Application.Features.Reports;

// GET /api/reports/my-sales (REPORTS.md "My sales — 2026-11-23"): the signed-in seller's own figures, never cost or profit.
public sealed record MySalesReportRequest : ReportPeriodRequest;

public sealed record MySalesReportResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    MySalesOrders Orders,
    MySalesTotals Sales,
    MySalesPayments Payments);

public sealed record MySalesOrders(int CreatedCount, int CompletedCount, int CancelledCount, int OpenCount);

public sealed record MySalesTotals(
    int OrderCount,
    decimal FulfilledValue,
    decimal ReturnValue,
    decimal NetSales,
    decimal AverageOrderValue);

public sealed record MySalesPayments(
    int PaymentCount,
    decimal ReceivedAmount,
    decimal OrderPaymentAmount,
    decimal DebtRepaymentAmount);

public interface IMySalesReportService
{
    Task<MySalesReportResponse> GetAsync(MySalesReportRequest request, CancellationToken token);
}

public sealed class MySalesReportRequestValidator : ReportPeriodValidator<MySalesReportRequest>;
