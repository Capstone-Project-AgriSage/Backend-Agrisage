using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 30: inventory_reservations. One open (ACTIVE / PARTIALLY_CONSUMED) reservation per Order.
internal sealed class InventoryReservationConfiguration : EntityConfiguration<InventoryReservation>
{
    protected override string TableName => "inventory_reservations";

    protected override void ConfigureEntity(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.Property(reservation => reservation.Status).HasMaxLength(30);
        builder.Property(reservation => reservation.ReleaseReason).HasMaxLength(500);

        builder.Ignore(reservation => reservation.RemainingQuantity);

        builder.HasReference<InventoryReservation, Store>(reservation => reservation.StoreId);
        builder.HasReference<InventoryReservation, Order>(reservation => reservation.OrderId);
        builder.HasUserReference(reservation => reservation.ReservedBy);
        builder.HasUserReference(reservation => reservation.ReleasedBy);
        builder.HasMany(reservation => reservation.Items).WithOne().HasForeignKey(item => item.InventoryReservationId);

        builder.HasIndex(reservation => reservation.OrderId);
        builder.HasIndex(reservation => reservation.OrderId, "ux_inventory_reservations_open_order")
            .IsUnique()
            .HasFilter("status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL")
            .HasDatabaseName("ux_inventory_reservations_open_order");
    }
}
