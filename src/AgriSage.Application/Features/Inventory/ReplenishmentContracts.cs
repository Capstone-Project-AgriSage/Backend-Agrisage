using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Inventory;

public sealed record ReplenishmentRequest : PaginationRequest
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? StoreProductId { get; init; }
    public string? Priority { get; init; }
    public string? Reason { get; init; }
    public string? DataStatus { get; init; }
    public int HistoryDays { get; init; } = 90;
    public int HorizonDays { get; init; } = 30;
    public string SortBy { get; init; } = "priority";
    public bool Descending { get; init; }
}
public sealed record ForecastPoint(DateOnly Date, decimal ExpectedBaseQuantity);
public sealed record SeasonalInfluence(Guid Id, string Name, string CropType, string? Region,
    DateOnly StartsOn, DateOnly EndsOn, decimal DemandFactor, long? TargetStockLevelBase);
public sealed record ReplenishmentItem(Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    long OnHandBaseQuantity, long ReservedBaseQuantity, long SellableBaseQuantity, long? MinStockLevelBase,
    long? TargetBaseQuantity, long? SuggestedBaseQuantity, long? SuggestedPurchaseQuantity,
    Guid? PurchasePackagingId, string? PurchaseUnit, long PurchaseConversionToBase,
    decimal? AverageDailySales, decimal? ExpectedDemandBaseQuantity, decimal? DaysOfStock,
    DateOnly? EstimatedStockoutDate, DateOnly? SuggestedOrderDate, int LeadTimeDays,
    long SoldBaseQuantity, int ObservedDays, int SaleDays, string Priority, string DataStatus,
    IReadOnlyList<string> ReasonCodes, string Explanation, IReadOnlyList<string> Warnings,
    IReadOnlyList<SeasonalInfluence> Seasons, IReadOnlyList<ForecastPoint> Forecast);
public sealed record ReplenishmentSummary(long TotalProducts, long OutOfStock, long HighPriority,
    long NeedsConfiguration, long InsufficientHistory, long SeasonalProducts);
public sealed record ReplenishmentPage(IReadOnlyList<ReplenishmentItem> Items, int Page, int PageSize,
    long TotalCount, ReplenishmentSummary Summary, DateTimeOffset GeneratedAt,
    DateOnly FromDate, DateOnly ToDate, int HorizonDays)
{
    public int TotalPages => (int)Math.Ceiling((decimal)TotalCount / PageSize);
}
public interface IReplenishmentService
{
    Task<ReplenishmentPage> GetAsync(string view, ReplenishmentRequest request, CancellationToken token);
}
public sealed class ReplenishmentRequestValidator : AbstractValidator<ReplenishmentRequest>
{
    public ReplenishmentRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.HistoryDays).InclusiveBetween(14, 365);
        RuleFor(r => r.HorizonDays).InclusiveBetween(1, 90);
        RuleFor(r => r.SortBy).Must(s => s?.Trim().ToLowerInvariant() is "priority" or "name" or "stockout" or "quantity");
        RuleFor(r => r.Priority).Must(s => s?.Trim().ToUpperInvariant() is null or "" or "HIGH" or "MEDIUM" or "LOW");
        RuleFor(r => r.DataStatus).Must(s => s?.Trim().ToUpperInvariant() is null or "" or "SUFFICIENT" or "INSUFFICIENT_HISTORY" or "NO_DEMAND");
        RuleFor(r => r.Reason).Must(s => s?.Trim().ToUpperInvariant() is null or "" or "OUT_OF_STOCK" or "LOW_STOCK" or "LEAD_TIME_RISK" or "SEASONAL" or "NEEDS_CONFIGURATION");
    }
}
