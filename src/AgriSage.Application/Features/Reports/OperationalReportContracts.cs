using FluentValidation;

namespace AgriSage.Application.Features.Reports;

public sealed record OrderReportRequest : ReportPeriodRequest { public string? GroupBy { get; init; } }
public sealed record PaymentReportRequest : ReportPeriodRequest { public string? GroupBy { get; init; } }
public sealed record PurchaseReportRequest : ReportPeriodRequest { public string? GroupBy { get; init; } }
public sealed record ReturnReportRequest : ReportPeriodRequest { public string? GroupBy { get; init; } }
public sealed record RefundReportRequest : ReportPeriodRequest { public string? GroupBy { get; init; } }

public sealed record OrderReportRow(string Key, string Label, int OrderCount, decimal OrderValue);
public sealed record OrderReportTotals(int OrderCount, decimal OrderValue);
public sealed record OrderReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy, IReadOnlyList<OrderReportRow> Rows, OrderReportTotals Totals);
public sealed record PaymentReportRow(string Key, string Label, int PaymentCount, decimal ReceivedAmount);
public sealed record PaymentReportTotals(int PaymentCount, decimal ReceivedAmount, decimal OrderPaymentAmount,
    decimal DebtRepaymentAmount, decimal RefundedAmount, decimal NetReceivedAmount);
public sealed record PaymentReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy, IReadOnlyList<PaymentReportRow> Rows, PaymentReportTotals Totals);
public sealed record PurchaseReportRow(string Key, string Label, int ReceiptCount, decimal PurchaseAmount);
public sealed record PurchaseReportTotals(int ReceiptCount, decimal PurchaseAmount);
public sealed record PurchaseReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy, IReadOnlyList<PurchaseReportRow> Rows, PurchaseReportTotals Totals);
public sealed record ReturnReportRow(string Key, string Label, int ReturnCount, decimal ReturnAmount, decimal DebtAdjustmentAmount, decimal RefundAmount);
public sealed record ReturnReportTotals(int ReturnCount, decimal ReturnAmount, decimal DebtAdjustmentAmount, decimal RefundAmount);
public sealed record ReturnReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy, IReadOnlyList<ReturnReportRow> Rows, ReturnReportTotals Totals);
public sealed record RefundReportRow(string Key, string Label, int RefundCount, decimal RefundedAmount);
public sealed record RefundReportTotals(int RefundCount, decimal RefundedAmount);
public sealed record RefundReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy, IReadOnlyList<RefundReportRow> Rows, RefundReportTotals Totals);
public sealed record CreditExposureRow(Guid FarmerProfileId, string FullName, string Status, decimal CreditLimit,
    decimal Outstanding, decimal ReservedCredit, decimal Exposure, decimal AvailableCredit, decimal? UtilizationPercent);
public sealed record CreditExposureTotals(decimal CreditLimit, decimal Outstanding, decimal ReservedCredit, decimal Exposure, decimal AvailableCredit);
public sealed record CreditExposureReportResponse(DateTimeOffset AsOf, IReadOnlyList<CreditExposureRow> Rows, CreditExposureTotals Totals);

public interface IOperationalReportService
{
    Task<OrderReportResponse> OrdersAsync(OrderReportRequest request, CancellationToken token);
    Task<PaymentReportResponse> PaymentsAsync(PaymentReportRequest request, CancellationToken token);
    Task<PurchaseReportResponse> PurchasesAsync(PurchaseReportRequest request, CancellationToken token);
    Task<ReturnReportResponse> ReturnsAsync(ReturnReportRequest request, CancellationToken token);
    Task<RefundReportResponse> RefundsAsync(RefundReportRequest request, CancellationToken token);
    Task<CreditExposureReportResponse> CreditAsync(CancellationToken token);
}

public sealed class OrderReportRequestValidator : ReportPeriodValidator<OrderReportRequest>
{
    public OrderReportRequestValidator() => RuleFor(r => r.GroupBy).Must(v => new[] { "STATUS", "SOURCE", "SETTLEMENT" }.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
}
public sealed class PaymentReportRequestValidator : ReportPeriodValidator<PaymentReportRequest>
{
    public PaymentReportRequestValidator() => RuleFor(r => r.GroupBy).Must(v => new[] { "DAY", "METHOD", "CONTEXT", "STAFF" }.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
}
public sealed class PurchaseReportRequestValidator : ReportPeriodValidator<PurchaseReportRequest>
{
    public PurchaseReportRequestValidator() => RuleFor(r => r.GroupBy).Must(v => new[] { "DAY", "SUPPLIER" }.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
}
public sealed class ReturnReportRequestValidator : ReportPeriodValidator<ReturnReportRequest>
{
    public ReturnReportRequestValidator() => RuleFor(r => r.GroupBy).Must(v => new[] { "DAY", "CUSTOMER" }.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
}
public sealed class RefundReportRequestValidator : ReportPeriodValidator<RefundReportRequest>
{
    public RefundReportRequestValidator() => RuleFor(r => r.GroupBy).Must(v => new[] { "DAY", "METHOD", "SOURCE" }.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
}
