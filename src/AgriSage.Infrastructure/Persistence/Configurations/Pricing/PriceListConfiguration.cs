using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Pricing;

// Table 18: price_lists. Period overlap is an Application rule; only the documented
// "one active Walk-in default Price List per Store" is a partial unique index.
internal sealed class PriceListConfiguration : EntityConfiguration<PriceList>
{
    protected override string TableName => "price_lists";

    protected override void ConfigureEntity(EntityTypeBuilder<PriceList> builder)
    {
        builder.Property(priceList => priceList.Code).HasMaxLength(50);
        builder.Property(priceList => priceList.Name).HasMaxLength(150);
        builder.Property(priceList => priceList.Description).HasMaxLength(500);
        builder.Property(priceList => priceList.IsWalkInDefault).HasDbDefault(false);
        builder.Property(priceList => priceList.Status).HasMaxLength(20);

        builder.HasReference<PriceList, Store>(priceList => priceList.StoreId);

        builder.HasIndex(priceList => new { priceList.StoreId, priceList.Status, priceList.EffectiveFrom })
            .IsDescending(false, false, true);
        builder.HasIndex(priceList => priceList.StoreId, "ux_price_lists_walk_in_default")
            .IsUnique()
            .HasFilter("is_walk_in_default AND status = 'ACTIVE' AND deleted_at IS NULL")
            .HasDatabaseName("ux_price_lists_walk_in_default");

        builder.HasCheck("effective_period", "effective_to IS NULL OR effective_to > effective_from");
    }
}
