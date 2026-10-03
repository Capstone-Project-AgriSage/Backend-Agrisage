using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Orders;

// Staff counter orders (task F1.2, FLOW_1 §4). Every write returns the order as read back after the save.
public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateCounterOrderRequest request, CancellationToken cancellationToken);

    Task<PagedResult<OrderListItem>> ListAsync(OrderListRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> AddItemAsync(Guid id, OrderItemRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> ChangeItemQuantityAsync(
        Guid id, Guid itemId, ChangeOrderItemQuantityRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> OverrideItemPriceAsync(
        Guid id, Guid itemId, OverrideOrderItemPriceRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> RestoreItemPriceAsync(Guid id, Guid itemId, CancellationToken cancellationToken);

    Task<OrderResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken);
}
