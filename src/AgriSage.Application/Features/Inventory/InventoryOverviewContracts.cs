using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Inventory;

public sealed record StockSummaryRequest : PaginationRequest
{
    public string? Search { get; init; }
    public Guid? CategoryId { get; init; }
    public bool LowStockOnly { get; init; }
    public bool? HasStock { get; init; }
}

public sealed record InventoryAlertsRequest : PaginationRequest
{
    public string? Type { get; init; }
    public int WithinDays { get; init; } = 30;
}

public sealed record StockSummaryItem(
    Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    long OnHandBaseQuantity, long ReservedBaseQuantity, long AvailableBaseQuantity,
    long SellableAvailableBaseQuantity, decimal StockValue, long? MinStockLevelBase,
    bool IsLowStock, DateOnly? NearestExpiryDate, int LotCount);

public sealed record InventoryAlertItem(
    string Type, Guid StoreProductId, string Sku, string ProductName,
    Guid? InventoryLotId, string? LotNumber, DateOnly? ExpiryDate, int? DaysToExpiry,
    string? LotStatus, long OnHandBaseQuantity, long ReservedBaseQuantity, long? MinStockLevelBase);

public sealed record ExpiredLotItem(Guid Id, string? LotNumber, DateOnly? ExpiryDate);

public sealed record ExpireDueLotsResponse(int ExpiredLotCount, IReadOnlyList<ExpiredLotItem> Lots);
