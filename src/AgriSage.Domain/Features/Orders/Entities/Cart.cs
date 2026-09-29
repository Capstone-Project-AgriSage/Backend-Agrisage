using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Orders.Entities;

// Online cart of a registered Farmer (walk-in customers have no cart).
// "One ACTIVE cart per Farmer per Store" is enforced by Application + partial unique index.
public sealed class Cart : SoftDeletableEntity
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    public Cart(Guid storeId, Guid farmerProfileId)
    {
        StoreId = storeId;
        FarmerProfileId = farmerProfileId;
        Status = CartStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid FarmerProfileId { get; private set; }

    public CartStatus Status { get; private set; }

    public Guid? ConvertedOrderId { get; private set; }

    public DateTimeOffset? ConvertedAt { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    // One line per Store Product + Packaging: adds the line or replaces its quantity.
    public CartItem SetItemQuantity(StoreProduct storeProduct, ProductPackaging productPackaging, long quantity)
    {
        EnsureActive();
        productPackaging.EnsureBelongsTo(storeProduct);

        var existing = ActiveItems.SingleOrDefault(i =>
            i.StoreProductId == storeProduct.Id && i.ProductPackagingId == productPackaging.Id);

        if (existing is not null)
        {
            existing.ChangeQuantity(quantity);
            return existing;
        }

        var item = new CartItem(Id, storeProduct.Id, productPackaging.Id, quantity);
        _items.Add(item);

        return item;
    }

    public void RemoveItem(Guid itemId, Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureActive();

        var item = ActiveItems.SingleOrDefault(i => i.Id == itemId)
            ?? throw new DomainException($"Cart item '{itemId}' was not found.");

        item.RemoveFromAggregate(deletedBy, deletedAt);
    }

    public void MarkConverted(Guid orderId, DateTimeOffset convertedAt)
    {
        EnsureActive();

        Status = CartStatus.Converted;
        ConvertedOrderId = orderId;
        ConvertedAt = convertedAt;
    }

    public void MarkAbandoned()
    {
        EnsureActive();
        Status = CartStatus.Abandoned;
    }

    private IEnumerable<CartItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private void EnsureActive()
    {
        if (Status != CartStatus.Active)
        {
            throw new DomainException($"Cart is {Status} and can no longer change.");
        }
    }
}
