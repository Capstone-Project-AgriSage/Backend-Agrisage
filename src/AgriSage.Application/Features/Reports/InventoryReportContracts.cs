using FluentValidation;

namespace AgriSage.Application.Features.Reports;

public record InventoryPeriodRequest
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}

public sealed record StockCardRequest : InventoryPeriodRequest
{
    public Guid StoreProductId { get; init; }
    public Guid? InventoryLotId { get; init; }
}

public sealed record InventoryMovementReportRequest : InventoryPeriodRequest
{
    public Guid? CategoryId { get; init; }
}

public sealed record InventoryValuationReportRequest
{
    public Guid? CategoryId { get; init; }
}

public sealed record StockCardReference(string Type, Guid Id, string? Number);
public sealed record StockCardLine(DateTimeOffset PostedAt, Guid MovementId, string MovementNumber,
    string MovementType, StockCardReference? Reference, string? LotNumber, long InBaseQuantity,
    long OutBaseQuantity, long BalanceBaseQuantity, decimal UnitCost);
public sealed record StockCardResponse(Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    DateOnly FromDate, DateOnly ToDate, long OpeningBaseQuantity, IReadOnlyList<StockCardLine> Lines,
    long ClosingBaseQuantity);

// Each movement bucket carries signed base quantity and signed cost value; totals contain values only.
public sealed record InventoryMovementAmount(long Quantity, decimal Value);
public sealed record InventoryMovementReportRow(Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    long OpeningQuantity, decimal OpeningValue, InventoryMovementAmount StockIn, InventoryMovementAmount ReturnIn,
    InventoryMovementAmount AdjustmentIn, InventoryMovementAmount Sale, InventoryMovementAmount AdjustmentOut,
    InventoryMovementAmount Reversal, long ClosingQuantity, decimal ClosingValue);
public sealed record InventoryMovementReportTotals(decimal OpeningValue, decimal StockIn, decimal ReturnIn,
    decimal AdjustmentIn, decimal Sale, decimal AdjustmentOut, decimal Reversal, decimal ClosingValue);
public sealed record InventoryMovementReportResponse(DateOnly FromDate, DateOnly ToDate,
    IReadOnlyList<InventoryMovementReportRow> Rows, InventoryMovementReportTotals Totals);

public sealed record InventoryValuationReportRow(Guid StoreProductId, string Sku, string ProductName, string Category,
    long OnHandBaseQuantity, decimal? AverageUnitCost, decimal StockValue, decimal ExpiredValue);
public sealed record InventoryValuationCategory(Guid CategoryId, string Name, decimal StockValue);
public sealed record InventoryValuationTotals(decimal StockValue, decimal ExpiredValue);
public sealed record InventoryValuationReportResponse(IReadOnlyList<InventoryValuationReportRow> Rows,
    IReadOnlyList<InventoryValuationCategory> ByCategory, InventoryValuationTotals Totals);

public interface IInventoryReportService
{
    Task<StockCardResponse> GetStockCardAsync(StockCardRequest request, CancellationToken cancellationToken);
    Task<InventoryMovementReportResponse> GetMovementAsync(InventoryMovementReportRequest request, CancellationToken cancellationToken);
    Task<InventoryValuationReportResponse> GetValuationAsync(InventoryValuationReportRequest request, CancellationToken cancellationToken);
}

public sealed class InventoryPeriodRequestValidator : AbstractValidator<InventoryPeriodRequest>
{
    public InventoryPeriodRequestValidator()
    {
        RuleFor(r => r.FromDate).NotNull().WithMessage("fromDate is required.");
        RuleFor(r => r.FromDate).Must(d => d != DateOnly.MinValue).WithMessage("fromDate must be after 0001-01-01.");
        RuleFor(r => r.ToDate).NotNull().WithMessage("toDate is required.");
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate.HasValue && r.ToDate.HasValue);
        RuleFor(r => r).Must(r => r.ToDate!.Value.DayNumber - r.FromDate!.Value.DayNumber + 1 <= 366)
            .When(r => r.FromDate.HasValue && r.ToDate.HasValue && r.ToDate >= r.FromDate)
            .WithName("ToDate").WithMessage("The period is at most 366 days.");
    }
}

public sealed class StockCardRequestValidator : AbstractValidator<StockCardRequest>
{
    public StockCardRequestValidator()
    {
        Include(new InventoryPeriodRequestValidator());
        RuleFor(r => r.StoreProductId).NotEmpty();
        RuleFor(r => r.InventoryLotId).NotEqual(Guid.Empty).When(r => r.InventoryLotId.HasValue);
    }
}

public sealed class InventoryMovementReportRequestValidator : AbstractValidator<InventoryMovementReportRequest>
{
    public InventoryMovementReportRequestValidator()
    {
        Include(new InventoryPeriodRequestValidator());
        RuleFor(r => r.CategoryId).NotEqual(Guid.Empty).When(r => r.CategoryId.HasValue);
    }
}

public sealed class InventoryValuationReportRequestValidator : AbstractValidator<InventoryValuationReportRequest>
{
    public InventoryValuationReportRequestValidator() =>
        RuleFor(r => r.CategoryId).NotEqual(Guid.Empty).When(r => r.CategoryId.HasValue);
}
