using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Orders;

// FLOW_2 §5 (task F2.3): the Farmer's online checkout and own orders. Lines always come from the ACTIVE cart.

// Source: FARMER_WEB | FARMER_MOBILE. DELIVERY needs exactly one of AddressId (one of the Farmer's own addresses) or
// DeliveryAddress; PICKUP takes neither.
public sealed record CheckoutRequest(
    string Source,
    string SettlementType,
    string FulfillmentType,
    Guid? AddressId = null,
    DeliveryAddressRequest? DeliveryAddress = null,
    string? Note = null);

// Status: enum text. FromDate/ToDate: Vietnam days on createdAt.
public sealed record MyOrderListRequest : PaginationRequest
{
    public string? Status { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }
}

public sealed record CancelMyOrderRequest(string? Reason = null);

public interface IMeOrderService
{
    Task<OrderResponse> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    Task<PagedResult<OrderListItem>> ListAsync(MyOrderListRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OrderResponse> CancelAsync(Guid id, CancelMyOrderRequest request, CancellationToken cancellationToken);
}
