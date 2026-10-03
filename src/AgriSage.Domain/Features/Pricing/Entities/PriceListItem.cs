using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Pricing.Entities;

public sealed class PriceListItem : SoftDeletableEntity
{
    private PriceListItem()
    {
    }

    public PriceListItem(
        Guid priceListId,
        StoreProduct storeProduct,
        ProductPackaging productPackaging,
        decimal sellingPrice)
    {
        productPackaging.EnsureBelongsTo(storeProduct);

        PriceListId = priceListId;
        StoreProductId = storeProduct.Id;
        ProductPackagingId = productPackaging.Id;
        ChangeSellingPrice(sellingPrice);
    }

    public Guid PriceListId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public StoreProduct StoreProduct { get; private set; } = null!;

    public Guid ProductPackagingId { get; private set; }

    public ProductPackaging ProductPackaging { get; private set; } = null!;

    public decimal SellingPrice { get; private set; }

    public void ChangeSellingPrice(decimal sellingPrice) => SellingPrice = Guard.NonNegativeMoney(sellingPrice);

    // UNIQUE(price_list_id, store_product_id, product_packaging_id) also counts deleted rows, so pricing a removed
    // pair again revives the old row with the new price instead of violating the index.
    public void Reinstate(decimal sellingPrice)
    {
        var price = Guard.NonNegativeMoney(sellingPrice);
        Restore();
        SellingPrice = price;
    }
}
