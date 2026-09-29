using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Pricing;

// Table 19: price_list_items.
internal sealed class PriceListItemConfiguration : EntityConfiguration<PriceListItem>
{
    protected override string TableName => "price_list_items";

    protected override void ConfigureEntity(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.Property(item => item.SellingPrice).IsMoney();

        builder.HasReference<PriceListItem, PriceList>(item => item.PriceListId);
        builder.HasOne(item => item.StoreProduct).WithMany().HasForeignKey(item => item.StoreProductId);
        builder.HasOne(item => item.ProductPackaging).WithMany().HasForeignKey(item => item.ProductPackagingId);

        builder.HasIndex(item => new { item.PriceListId, item.StoreProductId, item.ProductPackagingId }).IsUnique();

        builder.HasCheck("selling_price", "selling_price >= 0");
    }
}
