using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Reports;

public sealed record InventoryPlanningReportRequest : PaginationRequest
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public int HistoryDays { get; init; } = 90;
    public int WithinDays { get; init; } = 30;
    public string? Velocity { get; init; }
}
public sealed record InventoryVelocityItem(Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    long SoldBaseQuantity, long OnHandBaseQuantity, long SellableBaseQuantity, decimal? AverageDailySales,
    decimal? DaysOfStock, DateOnly? LastSaleDate, string Velocity, string DataStatus);
public sealed record InventoryVelocitySummary(long Products, long Fast, long Slow, long NoSales);
public sealed record InventoryVelocityPage(IReadOnlyList<InventoryVelocityItem> Items, int Page, int PageSize,
    long TotalCount, InventoryVelocitySummary Summary, DateOnly FromDate, DateOnly ToDate)
{
    public int TotalPages => (int)Math.Ceiling((decimal)TotalCount / PageSize);
}
public sealed record ExpiryRiskItem(Guid InventoryLotId, Guid StoreProductId, string Sku, string ProductName,
    string BaseUnit, string? LotNumber, DateOnly ExpiryDate, string LotStatus, long OnHandBaseQuantity,
    long ReservedBaseQuantity, long? AtRiskBaseQuantity, decimal? AtRiskValue, string DataStatus);
public sealed record ExpiryRiskSummary(long Lots, long KnownRiskLots, long UnknownRiskLots, decimal KnownRiskValue);
public sealed record ExpiryRiskPage(IReadOnlyList<ExpiryRiskItem> Items, int Page, int PageSize,
    long TotalCount, ExpiryRiskSummary Summary, int WithinDays)
{
    public int TotalPages => (int)Math.Ceiling((decimal)TotalCount / PageSize);
}
public interface IInventoryPlanningReportService
{
    Task<InventoryVelocityPage> VelocityAsync(InventoryPlanningReportRequest request, CancellationToken token);
    Task<ExpiryRiskPage> ExpiryRiskAsync(InventoryPlanningReportRequest request, CancellationToken token);
}
public interface IInventoryReportSpreadsheet
{
    byte[] Export(InventoryMovementReportResponse report);
}
public sealed class InventoryPlanningReportRequestValidator : AbstractValidator<InventoryPlanningReportRequest>
{
    public InventoryPlanningReportRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.HistoryDays).InclusiveBetween(14, 365);
        RuleFor(r => r.WithinDays).InclusiveBetween(1, 365);
        RuleFor(r => r.Velocity).Must(v => v?.Trim().ToUpperInvariant() is null or "" or "FAST" or "STABLE" or "SLOW" or "NO_SALES");
    }
}
