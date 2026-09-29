using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Orders.Entities;

// Selected packaging and quantity in a Cart. Not a price snapshot: prices are recalculated at checkout.
public sealed class CartItem : SoftDeletableChildEntity
{
    private CartItem()
    {
    }

    internal CartItem(Guid cartId, Guid storeProductId, Guid productPackagingId, long quantity)
    {
        CartId = cartId;
        StoreProductId = storeProductId;
        ProductPackagingId = productPackagingId;
        ChangeQuantity(quantity);
    }

    public Guid CartId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public StoreProduct StoreProduct { get; private set; } = null!;

    public Guid ProductPackagingId { get; private set; }

    public ProductPackaging ProductPackaging { get; private set; } = null!;

    // Quantity of the selected packaging (not base units).
    public long Quantity { get; private set; }

    internal void ChangeQuantity(long quantity) => Quantity = Guard.Positive(quantity);
}
