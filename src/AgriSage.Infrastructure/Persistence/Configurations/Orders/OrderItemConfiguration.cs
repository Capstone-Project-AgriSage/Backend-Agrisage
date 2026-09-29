using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Orders;

// Table 35: order_items. Authoritative commercial snapshot.
internal sealed class OrderItemConfiguration : EntityConfiguration<OrderItem>
{
    protected override string TableName => "order_items";

    protected override void ConfigureEntity(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Property(item => item.ProductSkuSnapshot).HasMaxLength(50);
        builder.Property(item => item.ProductNameSnapshot).HasMaxLength(255);
        builder.Property(item => item.PackagingNameSnapshot).HasMaxLength(150);
        builder.Property(item => item.SuggestedUnitPrice).IsMoney();
        builder.Property(item => item.UnitPrice).IsMoney();
        builder.Property(item => item.LineTotalAmount).IsMoney();
        builder.Property(item => item.PriceOverridden).HasDbDefault(false);
        builder.Property(item => item.OverrideReason).HasMaxLength(500);
        builder.Property(item => item.FulfilledBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.CancelledBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.Status).HasMaxLength(30);

        builder.Ignore(item => item.RemainingBaseQuantity);

        builder.HasOne(item => item.StoreProduct).WithMany().HasForeignKey(item => item.StoreProductId);
        builder.HasOne(item => item.ProductPackaging).WithMany().HasForeignKey(item => item.ProductPackagingId);
        builder.HasUserReference(item => item.OverriddenBy);

        builder.HasIndex(item => item.Status);

        builder.HasCheck("quantity", "quantity > 0");
        builder.HasCheck("conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
        builder.HasCheck("base_quantity", "base_quantity = quantity * conversion_to_base_snapshot");
        builder.HasCheck("suggested_unit_price", "suggested_unit_price >= 0");
        builder.HasCheck("unit_price", "unit_price >= 0");
        builder.HasCheck("line_total_amount", "line_total_amount >= 0");
        builder.HasCheck("fulfilled_base_quantity", "fulfilled_base_quantity >= 0");
        builder.HasCheck("cancelled_base_quantity", "cancelled_base_quantity >= 0");
        builder.HasCheck(
            "fulfilled_cancelled_within_base",
            "fulfilled_base_quantity + cancelled_base_quantity <= base_quantity");
        builder.HasCheck(
            "price_override",
            "NOT price_overridden OR (override_reason IS NOT NULL AND overridden_by IS NOT NULL)");
    }
}
