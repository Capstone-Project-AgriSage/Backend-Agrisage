using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 31: inventory_reservation_items.
internal sealed class InventoryReservationItemConfiguration : EntityConfiguration<InventoryReservationItem>
{
    protected override string TableName => "inventory_reservation_items";

    protected override void ConfigureEntity(EntityTypeBuilder<InventoryReservationItem> builder)
    {
        builder.Property(item => item.BaseQuantityConsumed).HasDbDefault(0L);
        builder.Property(item => item.BaseQuantityReleased).HasDbDefault(0L);

        builder.Ignore(item => item.RemainingQuantity);

        builder.HasReference<InventoryReservationItem, OrderItem>(item => item.OrderItemId);
        builder.HasReference<InventoryReservationItem, InventoryLot>(item => item.InventoryLotId);

        builder.HasIndex(item => item.InventoryReservationId);
        builder.HasIndex(item => new { item.InventoryReservationId, item.OrderItemId, item.InventoryLotId }, "ux_inventory_reservation_item_lot")
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_inventory_reservation_item_lot");

        builder.HasCheck("base_quantity_reserved", "base_quantity_reserved > 0");
        builder.HasCheck("base_quantity_consumed", "base_quantity_consumed >= 0");
        builder.HasCheck("base_quantity_released", "base_quantity_released >= 0");
        builder.HasCheck(
            "within_reserved",
            "base_quantity_consumed + base_quantity_released <= base_quantity_reserved");
    }
}
