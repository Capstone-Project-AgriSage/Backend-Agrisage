using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Inventory;

// Status: ACTIVE / QUARANTINED / EXPIRED / BLOCKED / DEPLETED. ExpiringBefore: lots whose expiry date is on or before it.
public sealed record InventoryLotListRequest : PaginationRequest
{
    public Guid? StoreProductId { get; init; }

    public string? Status { get; init; }

    public DateOnly? ExpiringBefore { get; init; }

    public bool? HasStock { get; init; }

    public string? Search { get; init; }
}

// Statuses an operator may set. DEPLETED is not one of them: it is the state of a lot with nothing left.
public sealed record ChangeLotStatusRequest(string Status);

// Type: STOCK_IN / SALE / RETURN_IN / ADJUSTMENT_IN / ADJUSTMENT_OUT / REVERSAL. FromDate / ToDate: Vietnam days (inclusive).
public sealed record StockMovementListRequest : PaginationRequest
{
    public string? Type { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public Guid? GoodsReceiptId { get; init; }
}

public sealed record InventoryLotResponse(
    Guid Id,
    Guid StoreProductId,
    Guid ProductId,
    string Sku,
    string ProductName,
    string? LotNumber,
    DateOnly? ManufacturingDate,
    DateOnly? ExpiryDate,
    bool IsExpired,
    string Status,
    long QuantityOnHand,
    long QuantityReserved,
    long QuantityAvailable,
    decimal? AverageUnitCost,
    decimal TotalCostValue);

public sealed record StockMovementListItem(
    Guid Id,
    string MovementNumber,
    string MovementType,
    string Status,
    DateTimeOffset OccurredAt,
    Guid? GoodsReceiptId,
    int ItemCount);

public sealed record StockMovementItemResponse(
    Guid Id,
    Guid InventoryLotId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    string Sku,
    string ProductName,
    long QuantityDeltaBase,
    decimal UnitCostSnapshot,
    decimal TotalCostSnapshot,
    long? QuantityOnHandAfter,
    decimal? TotalCostValueAfter,
    string? Note);

public sealed record StockMovementResponse(
    Guid Id,
    string MovementNumber,
    string MovementType,
    string Status,
    DateTimeOffset OccurredAt,
    Guid? GoodsReceiptId,
    Guid? OrderId,
    Guid? DeliveryId,
    Guid? StocktakeId,
    Guid? ReversalOfMovementId,
    string? ReasonCode,
    string? Reason,
    Guid CreatedBy,
    DateTimeOffset? PostedAt,
    Guid? PostedBy,
    IReadOnlyList<StockMovementItemResponse> Items);

public interface IInventoryService
{
    Task<PagedResult<InventoryLotResponse>> ListLotsAsync(InventoryLotListRequest request, CancellationToken cancellationToken);

    Task<InventoryLotResponse> GetLotAsync(Guid id, CancellationToken cancellationToken);

    Task<InventoryLotResponse> ChangeLotStatusAsync(Guid id, ChangeLotStatusRequest request, CancellationToken cancellationToken);

    Task<PagedResult<StockMovementListItem>> ListMovementsAsync(StockMovementListRequest request, CancellationToken cancellationToken);

    Task<StockMovementResponse> GetMovementAsync(Guid id, CancellationToken cancellationToken);
}
