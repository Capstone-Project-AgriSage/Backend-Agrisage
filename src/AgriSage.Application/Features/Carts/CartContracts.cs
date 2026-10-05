namespace AgriSage.Application.Features.Carts;

// FLOW_2 §4: the Farmer's cart. It never stores prices; every response recalculates them.

public sealed record AddCartItemRequest(Guid StoreProductId, Guid ProductPackagingId, long Quantity);

public sealed record UpdateCartItemRequest(long Quantity);

public sealed record CartItemResponse(
    Guid Id,
    Guid StoreProductId,
    Guid ProductPackagingId,
    string Sku,
    string ProductName,
    string PackagingName,
    string? ImageUrl,
    long Quantity,
    decimal? UnitPrice,
    decimal? LineTotalAmount,
    bool IsAvailable,
    string? UnavailableReason);

public sealed record CartResponse(
    Guid? Id,
    IReadOnlyList<CartItemResponse> Items,
    decimal SubtotalAmount,
    Guid? PriceListId);

public static class CartUnavailableReason
{
    public const string NotSellable = "NOT_SELLABLE";
    public const string NoPrice = "NO_PRICE";
}

public interface ICartService
{
    Task<CartResponse> GetAsync(CancellationToken cancellationToken);

    Task<CartResponse> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken);

    Task<CartResponse> UpdateItemAsync(Guid itemId, UpdateCartItemRequest request, CancellationToken cancellationToken);

    Task<CartResponse> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
