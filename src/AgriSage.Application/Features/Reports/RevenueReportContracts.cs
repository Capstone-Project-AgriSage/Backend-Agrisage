using AgriSage.Application.Common;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Reports;

public record ReportPeriodRequest
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

public record RevenueFilterRequest : ReportPeriodRequest
{
    public Guid? StoreProductId { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? StaffUserId { get; init; }
    public Guid? FarmerProfileId { get; init; }
    public Guid? CustomerGroupId { get; init; }
    public string? Source { get; init; }
    public string? SettlementType { get; init; }
}

public sealed record RevenueReportRequest : RevenueFilterRequest
{
    public string? GroupBy { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record RevenueSummaryRequest : RevenueFilterRequest;

public sealed record RevenueMetrics(int OrderCount, decimal FulfilledValue, decimal CostOfGoods,
    decimal GrossProfit, decimal ReturnValue, decimal NetSales, decimal AverageOrderValue, decimal? GrossMarginPercent);
public sealed record RevenueReportRow(string Key, string Label, int OrderCount, decimal FulfilledValue,
    decimal CostOfGoods, decimal GrossProfit, decimal ReturnValue, decimal NetSales,
    decimal AverageOrderValue, decimal? GrossMarginPercent);
public sealed record RevenueReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy,
    IReadOnlyList<RevenueReportRow> Rows, int Page, int PageSize, int TotalCount, int TotalPages, RevenueMetrics Totals);
public sealed record RevenueSummaryResponse(DateOnly FromDate, DateOnly ToDate, RevenueMetrics Current,
    DateOnly PreviousFromDate, DateOnly PreviousToDate, RevenueMetrics Previous, decimal? NetSalesChangePercent);

public interface IRevenueReportService
{
    Task<RevenueReportResponse> GetAsync(RevenueReportRequest request, CancellationToken token);
    Task<RevenueSummaryResponse> SummaryAsync(RevenueSummaryRequest request, CancellationToken token);
}

public class ReportPeriodValidator<T> : AbstractValidator<T> where T : ReportPeriodRequest
{
    public ReportPeriodValidator()
    {
        RuleFor(r => r.FromDate).NotNull();
        RuleFor(r => r.FromDate).GreaterThan(DateOnly.MinValue).When(r => r.FromDate is not null)
            .WithMessage("fromDate must have a representable Vietnam midnight in UTC.");
        RuleFor(r => r.ToDate).NotNull();
        RuleFor(r => r.ToDate).LessThan(DateOnly.MaxValue).When(r => r.ToDate is not null);
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate is not null && r.ToDate is not null);
        RuleFor(r => r).Must(r => r.ToDate!.Value.DayNumber - r.FromDate!.Value.DayNumber + 1 <= 366)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithName("ToDate")
            .WithMessage("The period is at most 366 days.");
    }
}

public class RevenueFilterValidator<T> : ReportPeriodValidator<T> where T : RevenueFilterRequest
{
    public RevenueFilterValidator()
    {
        RuleFor(r => r.StoreProductId).NotEqual(Guid.Empty).When(r => r.StoreProductId is not null);
        RuleFor(r => r.CategoryId).NotEqual(Guid.Empty).When(r => r.CategoryId is not null);
        RuleFor(r => r.StaffUserId).NotEqual(Guid.Empty).When(r => r.StaffUserId is not null);
        RuleFor(r => r.FarmerProfileId).NotEqual(Guid.Empty).When(r => r.FarmerProfileId is not null);
        RuleFor(r => r.CustomerGroupId).NotEqual(Guid.Empty).When(r => r.CustomerGroupId is not null);
        RuleFor(r => r.Source).Must(v => EnumText.TryParse<OrderSource>(v, out _)).When(r => r.Source is not null);
        RuleFor(r => r.SettlementType).Must(v => EnumText.TryParse<SettlementType>(v, out _)).When(r => r.SettlementType is not null);
    }
}

public sealed class RevenueReportRequestValidator : RevenueFilterValidator<RevenueReportRequest>
{
    public static readonly string[] GroupBys = ["DAY", "WEEK", "MONTH", "PRODUCT", "CATEGORY", "STAFF", "CUSTOMER", "CUSTOMER_GROUP", "SOURCE", "SETTLEMENT"];
    public RevenueReportRequestValidator()
    {
        RuleFor(r => r.GroupBy).Must(v => GroupBys.Contains(v!.Trim().ToUpperInvariant())).When(r => r.GroupBy is not null);
        RuleFor(r => r.Page).GreaterThan(0);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class RevenueSummaryRequestValidator : RevenueFilterValidator<RevenueSummaryRequest>
{
    public RevenueSummaryRequestValidator()
    {
        RuleFor(r => r).Must(r => r.FromDate!.Value.DayNumber > r.ToDate!.Value.DayNumber - r.FromDate.Value.DayNumber + 1)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithName("FromDate")
            .WithMessage("The preceding comparison period must be representable.");
    }
}
