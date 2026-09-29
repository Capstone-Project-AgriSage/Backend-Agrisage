using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Products.Entities;

public sealed class StoreProduct : SoftDeletableEntity
{
    private StoreProduct()
    {
    }

    public StoreProduct(Guid storeId, Guid productId, string? storeSku = null, long? minStockLevelBase = null)
    {
        StoreId = storeId;
        ProductId = productId;
        UpdateSettings(storeSku, minStockLevelBase);
        IsSellable = true;
        IsActive = true;
    }

    public Guid StoreId { get; private set; }

    public Guid ProductId { get; private set; }

    public Product Product { get; private set; } = null!;

    public string? StoreSku { get; private set; }

    // Base-unit quantity.
    public long? MinStockLevelBase { get; private set; }

    public bool IsSellable { get; private set; }

    public bool IsActive { get; private set; }

    public void UpdateSettings(string? storeSku, long? minStockLevelBase)
    {
        StoreSku = storeSku;
        MinStockLevelBase = minStockLevelBase;
    }

    public void MarkSellable() => IsSellable = true;

    public void MarkNotSellable() => IsSellable = false;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
