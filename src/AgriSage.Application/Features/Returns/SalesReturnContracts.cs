using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Returns;

public sealed record ReturnItemRequest(Guid OrderItemId, long ReturnedBaseQuantity, string ReasonCode,
    Guid? DeliveryItemLotAllocationId = null, Guid? OriginalStockMovementItemId = null);
public sealed record CreateReturnRequest(Guid OrderId, IReadOnlyList<ReturnItemRequest> Items,
    string? ReasonSummary = null, string? Note = null);
public sealed record ReturnReasonRequest(string Reason);
public sealed record ReturnInspectionRequest(string ConditionStatus, string? InspectionNote = null);
public sealed record SalesReturnListRequest : PaginationRequest
{
    public string? Status { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? FarmerProfileId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string? Search { get; init; }
}

public sealed record ReturnableSource(Guid? DeliveryItemId, Guid? DeliveryItemLotAllocationId,
    Guid? OriginalStockMovementItemId, Guid InventoryLotId, string? LotNumber, DateOnly? ExpiryDate,
    long FulfilledBaseQuantity, long AlreadyReturnedBaseQuantity, long ReturnableBaseQuantity,
    decimal UnitPrice, long ConversionToBase, decimal? OriginalCogsUnitCost);
public sealed record ReturnableOrderItem(Guid OrderItemId, string Sku, string ProductName,
    long FulfilledBaseQuantity, long AlreadyReturnedBaseQuantity, long ReturnableBaseQuantity,
    decimal UnitPrice, long ConversionToBase, IReadOnlyList<ReturnableSource> Sources);
public sealed record ReturnableResponse(Guid OrderId, string OrderNumber, string FulfillmentType, IReadOnlyList<ReturnableOrderItem> Items);
public sealed record SalesReturnListItem(Guid Id, string ReturnNumber, Guid OrderId, string OrderNumber,
    Guid? FarmerProfileId, string CustomerName, string Status, decimal TotalReturnAmount, decimal TotalRefundAmount, DateTimeOffset RequestedAt);
public sealed record SalesReturnItemResponse(Guid Id, Guid OrderItemId, Guid? DeliveryItemId,
    Guid? DeliveryItemLotAllocationId, Guid? OriginalStockMovementItemId, Guid InventoryLotId,
    long ReturnedBaseQuantity, decimal SellingUnitPriceSnapshot, long ConversionToBaseSnapshot,
    decimal ReturnValue, decimal? OriginalCogsUnitCost, decimal? ReturnInventoryCostValue, string ReasonCode,
    string ConditionStatus, string InventoryDisposition, string? InspectionNote,
    Guid? ReturnStockMovementId, Guid? DebtAdjustmentTransactionId);
public sealed record SalesReturnResponse(Guid Id, Guid StoreId, string ReturnNumber, Guid OrderId, string OrderNumber,
    Guid? FarmerProfileId, string CustomerName, string Status, Guid RequestedBy, DateTimeOffset RequestedAt,
    Guid? ApprovedBy, DateTimeOffset? ApprovedAt, Guid? ReceivedBy, DateTimeOffset? ReceivedAt,
    Guid? InspectedBy, DateTimeOffset? InspectedAt, DateTimeOffset? CompletedAt,
    Guid? CancelledBy, DateTimeOffset? CancelledAt, string? CancelReason, string? ReasonSummary, string? Note,
    decimal TotalReturnAmount, decimal TotalDebtAdjustment, decimal TotalRefundAmount,
    IReadOnlyList<SalesReturnItemResponse> Items, IReadOnlyList<RefundResponse> Refunds);

public interface ISalesReturnService
{
    Task<ReturnableResponse> ReturnableAsync(Guid orderId, CancellationToken token);
    Task<SalesReturnResponse> CreateAsync(CreateReturnRequest request, CancellationToken token);
    Task<PagedResult<SalesReturnListItem>> ListAsync(SalesReturnListRequest request, CancellationToken token);
    Task<SalesReturnResponse> GetAsync(Guid id, CancellationToken token);
    Task<SalesReturnResponse> AddItemAsync(Guid id, ReturnItemRequest request, CancellationToken token);
    Task<SalesReturnResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken token);
    Task<SalesReturnResponse> ApproveAsync(Guid id, CancellationToken token);
    Task<SalesReturnResponse> RejectAsync(Guid id, ReturnReasonRequest request, CancellationToken token);
    Task<SalesReturnResponse> CancelAsync(Guid id, ReturnReasonRequest request, CancellationToken token);
    Task<SalesReturnResponse> ReceiveAsync(Guid id, CancellationToken token);
    Task<SalesReturnResponse> InspectAsync(Guid id, Guid itemId, ReturnInspectionRequest request, CancellationToken token);
    Task<SalesReturnResponse> CompleteInspectionAsync(Guid id, CancellationToken token);
}

public interface IMySalesReturnService
{
    Task<ReturnableResponse> ReturnableAsync(Guid orderId, CancellationToken token);
    Task<SalesReturnResponse> CreateAsync(CreateReturnRequest request, CancellationToken token);
    Task<PagedResult<SalesReturnListItem>> ListAsync(PaginationRequest request, CancellationToken token);
    Task<SalesReturnResponse> GetAsync(Guid id, CancellationToken token);
    Task<SalesReturnResponse> CancelAsync(Guid id, ReturnReasonRequest request, CancellationToken token);
}
