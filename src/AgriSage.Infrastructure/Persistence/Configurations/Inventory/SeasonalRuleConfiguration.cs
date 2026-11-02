using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

internal sealed class SeasonalRuleConfiguration : EntityConfiguration<SeasonalRule>
{
    protected override string TableName => "inventory_seasonal_rules";
    protected override void ConfigureEntity(EntityTypeBuilder<SeasonalRule> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();
        builder.Property(r => r.CropType).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Region).HasMaxLength(150);
        builder.Property(r => r.DemandFactor).HasPrecision(6, 3);
        builder.Property(r => r.Version).IsConcurrencyToken().HasDbDefault(0L);
        builder.Property(r => r.IsActive).HasDbDefault(true);
        builder.HasOne<Store>().WithMany().HasForeignKey(r => r.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.SeasonalRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(r => new { r.StoreId, r.IsActive, r.StartsOn });
        builder.ToTable(t => {
            t.HasCheckConstraint("ck_seasonal_rule_dates", "ends_on >= starts_on");
            t.HasCheckConstraint("ck_seasonal_rule_prepare", "prepare_days BETWEEN 0 AND 90");
            t.HasCheckConstraint("ck_seasonal_rule_factor", "demand_factor BETWEEN 0.1 AND 5");
        });
    }
}
internal sealed class SeasonalRuleItemConfiguration : EntityConfiguration<SeasonalRuleItem>
{
    protected override string TableName => "inventory_seasonal_rule_items";
    protected override void ConfigureEntity(EntityTypeBuilder<SeasonalRuleItem> builder)
    {
        builder.HasOne<StoreProduct>().WithMany().HasForeignKey(i => i.StoreProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.SeasonalRuleId, i.StoreProductId }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.ToTable(t => t.HasCheckConstraint("ck_seasonal_item_target", "target_stock_level_base >= 0"));
    }
}
