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

        builder.HasReference<StoreProduct, Store>(storeProduct => storeProduct.StoreId);
        builder.HasOne(storeProduct => storeProduct.Product).WithMany().HasForeignKey(storeProduct => storeProduct.ProductId);

        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.ProductId }).IsUnique();
        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.IsActive, storeProduct.IsSellable });
    }
}
