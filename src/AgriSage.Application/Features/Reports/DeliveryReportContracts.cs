using FluentValidation;

namespace AgriSage.Application.Features.Reports;

// GET /api/reports/deliveries (FLOW_2 §9, task F2.7). FromDate/ToDate: Vietnam days, at most 366. GroupBy: DAY (default) | STAFF.
public sealed record DeliveryReportRequest
{
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? GroupBy { get; init; }
}

public sealed record DeliveryReportRow(
    string Key,
    string Label,
    int Deliveries,
    int Attempts,
    int Successful,
    int Partial,
    int Failed,
    decimal SuccessRate);

public sealed record DeliveryReportTotals(int Deliveries, int Attempts, int Successful, int Partial, int Failed, decimal SuccessRate);

public sealed record FailureReasonCount(string Code, int Count);

public sealed record IncidentTypeCount(string IncidentType, int Open, int Resolved);

public sealed record DeliveryReportResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    string GroupBy,
    IReadOnlyList<DeliveryReportRow> Rows,
    IReadOnlyList<FailureReasonCount> FailureReasons,
    IReadOnlyList<IncidentTypeCount> Incidents,
    DeliveryReportTotals Totals);

public interface IDeliveryReportService
{
    Task<DeliveryReportResponse> GetAsync(DeliveryReportRequest request, CancellationToken cancellationToken);
}

public sealed class DeliveryReportRequestValidator : AbstractValidator<DeliveryReportRequest>
{
    public static readonly string[] GroupBys = ["DAY", "STAFF"];

    public DeliveryReportRequestValidator()
    {
        RuleFor(r => r.FromDate).NotNull().WithMessage("fromDate is required.");
        RuleFor(r => r.ToDate).NotNull().WithMessage("toDate is required.");
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithMessage("toDate must not be before fromDate.");
        RuleFor(r => r).Must(r => r.ToDate!.Value.DayNumber - r.FromDate!.Value.DayNumber + 1 <= SalesReportRequestValidator.MaxDays)
            .When(r => r.FromDate is not null && r.ToDate is not null && r.ToDate >= r.FromDate)
            .WithName("ToDate").WithMessage($"The period is at most {SalesReportRequestValidator.MaxDays} days.");
        RuleFor(r => r.GroupBy)
            .Must(g => GroupBys.Contains(g!.Trim().ToUpperInvariant()))
            .When(r => !string.IsNullOrWhiteSpace(r.GroupBy))
            .WithMessage("groupBy must be DAY or STAFF.");
    }
}
