using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.GoodsReceipts;

// A line of a goods receipt. The conversion to the base unit is taken from the packaging, never from the client.
public sealed record GoodsReceiptItemRequest(
    Guid StoreProductId,
    Guid ProductPackagingId,
    long ReceivedQuantity,
    decimal PurchaseUnitCost,
    string? SupplierLotNumber = null,
    DateOnly? ManufacturingDate = null,
    DateOnly? ExpiryDate = null,
    string? Note = null);

public sealed record UpdateGoodsReceiptItemRequest(
    long ReceivedQuantity,
    decimal PurchaseUnitCost,
    string? SupplierLotNumber = null,
    DateOnly? ManufacturingDate = null,
    DateOnly? ExpiryDate = null,
    string? Note = null);

// ReceivedAt defaults to now. The receipt number is generated (GR-yyyyMMdd-NNNN) and the receiver is the caller.
public sealed record CreateGoodsReceiptRequest(
    Guid SupplierId,
    DateTimeOffset? ReceivedAt = null,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null,
    string? Note = null,
    IReadOnlyList<GoodsReceiptItemRequest>? Items = null);

public sealed record UpdateGoodsReceiptRequest(
    Guid SupplierId,
    DateTimeOffset ReceivedAt,
    string? SupplierInvoiceNumber = null,
    DateOnly? SupplierInvoiceDate = null,
    string? Note = null);

public sealed record CancelGoodsReceiptRequest(string? Reason = null);

// Status: DRAFT / CONFIRMED / CANCELLED. FromDate / ToDate are Vietnam calendar days (inclusive) of received_at.
public sealed record GoodsReceiptListRequest : PaginationRequest
{
    public string? Status { get; init; }

    public Guid? SupplierId { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? Search { get; init; }
}

public sealed record GoodsReceiptItemResponse(
    Guid Id,
    Guid StoreProductId,
    string Sku,
    string ProductName,
    Guid ProductPackagingId,
    string UnitName,
    string? PackagingName,
    long ReceivedQuantity,
    long ConversionToBaseSnapshot,
    long BaseQuantity,
    decimal PurchaseUnitCost,
    decimal BaseUnitCost,
    decimal LineTotalAmount,
    string? SupplierLotNumber,
    DateOnly? ManufacturingDate,
    DateOnly? ExpiryDate,
    Guid? InventoryLotId,
    string? Note);

public sealed record GoodsReceiptResponse(
    Guid Id,
    string ReceiptNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierInvoiceNumber,
    DateOnly? SupplierInvoiceDate,
    DateTimeOffset ReceivedAt,
    Guid ReceivedBy,
    string SourceType,
    string Status,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string? Note,
    DateTimeOffset? ConfirmedAt,
    Guid? ConfirmedBy,
    DateTimeOffset? CancelledAt,
    Guid? CancelledBy,
    string? CancelReason,
    Guid? StockMovementId,
    IReadOnlyList<GoodsReceiptItemResponse> Items);

public sealed record GoodsReceiptListItem(
    Guid Id,
    string ReceiptNumber,
    Guid SupplierId,
    string SupplierName,
    string? SupplierInvoiceNumber,
    DateTimeOffset ReceivedAt,
    string Status,
    decimal TotalAmount,
    int ItemCount);

public interface IGoodsReceiptService
{
    Task<GoodsReceiptResponse> CreateAsync(CreateGoodsReceiptRequest request, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<GoodsReceiptListItem>> ListAsync(GoodsReceiptListRequest request, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> UpdateHeaderAsync(Guid id, UpdateGoodsReceiptRequest request, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> AddItemAsync(Guid id, GoodsReceiptItemRequest request, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> UpdateItemAsync(
        Guid id, Guid itemId, UpdateGoodsReceiptItemRequest request, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken);

    Task<GoodsReceiptResponse> CancelAsync(Guid id, CancelGoodsReceiptRequest request, CancellationToken cancellationToken);

    // Soft delete of a DRAFT receipt.
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    // Creates or finds the lots, adds the stock at the weighted average cost, posts a STOCK_IN movement and marks the
    // receipt CONFIRMED, all in one transaction. Admin and Store Owner only.
    Task<GoodsReceiptResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken);
}
