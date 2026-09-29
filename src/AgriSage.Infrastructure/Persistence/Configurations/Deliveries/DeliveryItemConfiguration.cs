using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 39: delivery_items.
internal sealed class DeliveryItemConfiguration : EntityConfiguration<DeliveryItem>
{
    protected override string TableName => "delivery_items";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryItem> builder)
    {
        builder.Property(item => item.DeliveredBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.CancelledBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.Status).HasMaxLength(30);

        builder.Ignore(item => item.RemainingBaseQuantity);

        builder.HasReference<DeliveryItem, OrderItem>(item => item.OrderItemId);
        builder.HasMany(item => item.LotAllocations).WithOne().HasForeignKey(allocation => allocation.DeliveryItemId);

        builder.HasCheck("planned_quantity", "planned_quantity > 0");
        builder.HasCheck("conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
        builder.HasCheck("planned_base_quantity", "planned_base_quantity = planned_quantity * conversion_to_base_snapshot");
        builder.HasCheck("delivered_base_quantity", "delivered_base_quantity >= 0");
        builder.HasCheck("cancelled_base_quantity", "cancelled_base_quantity >= 0");
        builder.HasCheck(
            "delivered_cancelled_within_planned",
            "delivered_base_quantity + cancelled_base_quantity <= planned_base_quantity");
    }
}
