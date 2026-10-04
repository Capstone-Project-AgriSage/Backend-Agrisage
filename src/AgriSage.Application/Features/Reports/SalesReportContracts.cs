using FluentValidation;

namespace AgriSage.Application.Features.Reports;

// FLOW_1 §10. FromDate and ToDate are Vietnam calendar days (both included), at most 366 days; GroupBy = DAY (default),
// PRODUCT, STAFF or CUSTOMER_GROUP.
public sealed record SalesReportRequest
{
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? GroupBy { get; init; }
}

public sealed record SalesReportRow(
    string Key,
    string Label,
    int OrderCount,
    decimal FulfilledValue,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal ReturnValue,
    decimal NetSales);

// OrderCount of the totals counts each order once, whichever rows it appears in; the money columns add up the rows.
public sealed record SalesReportTotals(
    int OrderCount,
    decimal FulfilledValue,
    decimal CostOfGoods,
    decimal GrossProfit,
    decimal ReturnValue,
    decimal NetSales);

public sealed record SalesReportResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    string GroupBy,
    IReadOnlyList<SalesReportRow> Rows,
    SalesReportTotals Totals);

public interface ISalesReportService
{
    Task<SalesReportResponse> GetAsync(SalesReportRequest request, CancellationToken cancellationToken);
}

public sealed class SalesReportRequestValidator : AbstractValidator<SalesReportRequest>
{
    public const int MaxDays = 366;

    public static readonly string[] GroupBys = ["DAY", "PRODUCT", "STAFF", "CUSTOMER_GROUP"];

    public SalesReportRequestValidator()
    {
        RuleFor(r => r.FromDate).NotNull().WithMessage("fromDate is required.");
        RuleFor(r => r.ToDate).NotNull().WithMessage("toDate is required.");
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithMessage("toDate must not be before fromDate.");
        RuleFor(r => r).Must(r => r.ToDate!.Value.DayNumber - r.FromDate!.Value.DayNumber + 1 <= MaxDays)
            .When(r => r.FromDate is not null && r.ToDate is not null && r.ToDate >= r.FromDate)
            .WithName("ToDate").WithMessage($"The period is at most {MaxDays} days.");
        RuleFor(r => r.GroupBy)
            .Must(g => GroupBys.Contains(g!.Trim().ToUpperInvariant()))
            .When(r => !string.IsNullOrWhiteSpace(r.GroupBy))
            .WithMessage("groupBy must be DAY, PRODUCT, STAFF or CUSTOMER_GROUP.");
    }
}
