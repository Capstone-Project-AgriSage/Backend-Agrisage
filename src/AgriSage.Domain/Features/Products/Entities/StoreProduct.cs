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

    public bool ReplenishmentEnabled { get; private set; } = true;
    public long? TargetStockLevelBase { get; private set; }
    public int LeadTimeDays { get; private set; } = 7;
    public long SafetyStockBase { get; private set; }
    public int TargetCoverageDays { get; private set; } = 14;

    public void ConfigureReplenishment(bool enabled, long? target, int leadTimeDays, long safety, int coverageDays)
    {
        if (target < 0 || safety < 0 || leadTimeDays is < 0 or > 365 || coverageDays is < 1 or > 90
            || (target.HasValue && MinStockLevelBase.HasValue && target < MinStockLevelBase))
            throw new AgriSage.Domain.Common.Exceptions.DomainException("Invalid replenishment settings: target must cover minimum stock and quantities must be nonnegative.");
        ReplenishmentEnabled = enabled;
        TargetStockLevelBase = target;
        LeadTimeDays = leadTimeDays;
        SafetyStockBase = safety;
        TargetCoverageDays = coverageDays;
    }

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
