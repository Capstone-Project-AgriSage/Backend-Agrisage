using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 16: store_products.
internal sealed class StoreProductConfiguration : EntityConfiguration<StoreProduct>
{
    protected override string TableName => "store_products";

    protected override void ConfigureEntity(EntityTypeBuilder<StoreProduct> builder)
    {
        builder.Property(storeProduct => storeProduct.StoreSku).HasMaxLength(50);
        builder.Property(storeProduct => storeProduct.IsSellable).HasDbDefault(true);
        builder.Property(storeProduct => storeProduct.IsActive).HasDbDefault(true);
        builder.Property(p => p.ReplenishmentEnabled).HasDbDefault(true);
        builder.Property(p => p.LeadTimeDays).HasDbDefault(7);
        builder.Property(p => p.TargetCoverageDays).HasDbDefault(14);
        builder.Property(p => p.SafetyStockBase).HasDbDefault(0L);
        builder.ToTable(t => t.HasCheckConstraint("ck_store_product_replenishment",
            "(min_stock_level_base IS NULL OR min_stock_level_base >= 0) AND (target_stock_level_base IS NULL OR (target_stock_level_base >= 0 AND (min_stock_level_base IS NULL OR target_stock_level_base >= min_stock_level_base))) AND lead_time_days BETWEEN 0 AND 365 AND safety_stock_base >= 0 AND target_coverage_days BETWEEN 1 AND 90"));

        builder.HasReference<StoreProduct, Store>(storeProduct => storeProduct.StoreId);
        builder.HasOne(storeProduct => storeProduct.Product).WithMany().HasForeignKey(storeProduct => storeProduct.ProductId);

        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.ProductId }).IsUnique();
        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.IsActive, storeProduct.IsSellable });
    }
}
