using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Orders;

// FLOW_1 §4 and api-flows README §2 (OrderResponse is shared with L2).
public sealed record DeliveryAddressRequest(
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string Province,
    string? Ward = null,
    string? District = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

// Price: UnitPrice differing from the suggested price needs OverrideReason (staff only); otherwise it is ignored.
public sealed record OrderItemRequest(
    Guid StoreProductId,
    Guid ProductPackagingId,
    long Quantity,
    decimal? UnitPrice = null,
    string? OverrideReason = null);

// CustomerType: REGISTERED / WALK_IN. A walk-in name is optional (blank → "Khách lẻ").
public sealed record CreateCounterOrderRequest(
    string CustomerType,
    string SettlementType,
    string FulfillmentType,
    IReadOnlyList<OrderItemRequest> Items,
    Guid? FarmerProfileId = null,
    string? CustomerName = null,
    string? CustomerPhone = null,
    Guid? AddressId = null,
    DeliveryAddressRequest? DeliveryAddress = null,
    string? Note = null);

// Only while PENDING_CONFIRMATION. Null note = unchanged, blank note = cleared. At most one of the two addresses.
public sealed record UpdateOrderRequest(
    Guid? AddressId = null,
    DeliveryAddressRequest? DeliveryAddress = null,
    string? Note = null);

public sealed record ChangeOrderItemQuantityRequest(long Quantity);

public sealed record OverrideOrderItemPriceRequest(decimal UnitPrice, string Reason);

// Status, CustomerType, SettlementType, FulfillmentType, Source: enum text. FromDate/ToDate: Vietnam days on createdAt.
// Search: order number, customer name or phone.
public sealed record OrderListRequest : PaginationRequest
{
    public string? Status { get; init; }

    public string? CustomerType { get; init; }

    public string? SettlementType { get; init; }

    public string? FulfillmentType { get; init; }

    public string? Source { get; init; }

    public Guid? FarmerProfileId { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? Search { get; init; }
}

public sealed record DeliveryAddressResponse(
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string? Ward,
    string? District,
    string Province,
    decimal? Latitude,
    decimal? Longitude);

public sealed record OrderItemResponse(
    Guid Id,
    Guid StoreProductId,
    Guid ProductPackagingId,
    string Sku,
    string ProductName,
    string PackagingName,
    long Quantity,
    long ConversionToBase,
    long BaseQuantity,
    decimal SuggestedUnitPrice,
    decimal UnitPrice,
    decimal LineTotalAmount,
    bool PriceOverridden,
    string? OverrideReason,
    Guid? OverriddenBy,
    long FulfilledBaseQuantity,
    long CancelledBaseQuantity,
    long RemainingBaseQuantity,
    string Status);

public sealed record OrderResponse(
    Guid Id,
    string OrderNumber,
    string Source,
    string CustomerType,
    Guid? FarmerProfileId,
    string CustomerName,
    string? CustomerPhone,
    Guid? CustomerGroupId,
    Guid? PriceListId,
    string SettlementType,
    int? CreditTermDays,
    string FulfillmentType,
    DeliveryAddressResponse? DeliveryAddress,
    string Status,
    decimal SubtotalAmount,
    decimal TotalAmount,
    string? Note,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt,
    Guid? PickupCompletedBy,
    DateTimeOffset? PickupCompletedAt,
    DateTimeOffset? CompletedAt,
    Guid? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    long Version,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record OrderListItem(
    Guid Id,
    string OrderNumber,
    string Source,
    string CustomerType,
    string CustomerName,
    string? CustomerPhone,
    string SettlementType,
    string FulfillmentType,
    string Status,
    decimal TotalAmount,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt);
