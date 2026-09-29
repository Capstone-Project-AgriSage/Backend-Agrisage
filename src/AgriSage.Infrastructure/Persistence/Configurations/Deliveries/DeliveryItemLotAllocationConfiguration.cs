using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 40: delivery_item_lot_allocations.
internal sealed class DeliveryItemLotAllocationConfiguration : EntityConfiguration<DeliveryItemLotAllocation>
{
    protected override string TableName => "delivery_item_lot_allocations";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryItemLotAllocation> builder)
    {
        builder.Property(allocation => allocation.DeliveredBaseQuantity).HasDbDefault(0L);
        builder.Property(allocation => allocation.ReleasedBaseQuantity).HasDbDefault(0L);
        builder.Property(allocation => allocation.Status).HasMaxLength(30);

        builder.Ignore(allocation => allocation.UndeliveredQuantity);
        builder.Ignore(allocation => allocation.CommittedQuantity);

        builder.HasReference<DeliveryItemLotAllocation, InventoryLot>(allocation => allocation.InventoryLotId);
        builder.HasReference<DeliveryItemLotAllocation, InventoryReservationItem>(
            allocation => allocation.InventoryReservationItemId);

        builder.HasCheck("allocated_base_quantity", "allocated_base_quantity > 0");
        builder.HasCheck("delivered_base_quantity", "delivered_base_quantity >= 0");
        builder.HasCheck("released_base_quantity", "released_base_quantity >= 0");
        builder.HasCheck(
            "within_allocated",
            "delivered_base_quantity + released_base_quantity <= allocated_base_quantity");
    }
}
