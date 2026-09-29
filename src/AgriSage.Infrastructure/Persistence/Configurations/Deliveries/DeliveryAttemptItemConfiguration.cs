using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 42: delivery_attempt_items. The DB only enforces delivered + failed <= attempted; equality is
// enforced by the Domain when the attempt completes (database design §35.14).
internal sealed class DeliveryAttemptItemConfiguration : EntityConfiguration<DeliveryAttemptItem>
{
    protected override string TableName => "delivery_attempt_items";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryAttemptItem> builder)
    {
        builder.Property(item => item.DeliveredBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.FailedBaseQuantity).HasDbDefault(0L);
        builder.Property(item => item.Note).HasMaxLength(500);

        builder.HasReference<DeliveryAttemptItem, DeliveryItemLotAllocation>(item => item.DeliveryItemLotAllocationId);

        builder.HasIndex(item => item.DeliveryAttemptId);
        builder.HasIndex(item => new { item.DeliveryAttemptId, item.DeliveryItemLotAllocationId }, "ux_delivery_attempt_allocation")
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_delivery_attempt_allocation");

        builder.HasCheck("attempted_base_quantity", "attempted_base_quantity > 0");
        builder.HasCheck("delivered_base_quantity", "delivered_base_quantity >= 0");
        builder.HasCheck("failed_base_quantity", "failed_base_quantity >= 0");
        builder.HasCheck(
            "delivered_failed_within_attempted",
            "delivered_base_quantity + failed_base_quantity <= attempted_base_quantity");
    }
}
