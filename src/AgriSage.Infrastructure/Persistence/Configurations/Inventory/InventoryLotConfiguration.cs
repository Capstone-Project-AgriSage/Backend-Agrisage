using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 24: inventory_lots. The logical-lot unique index (lower(lot_number), NULLS NOT DISTINCT) is raw SQL
// (PostgreSqlRawIndexes.InventoryLotsLogicalLot); the no-lot bucket index is modelled here.
internal sealed class InventoryLotConfiguration : EntityConfiguration<InventoryLot>
{
    protected override string TableName => "inventory_lots";

    protected override void ConfigureEntity(EntityTypeBuilder<InventoryLot> builder)
    {
        builder.Property(lot => lot.LotNumber).HasMaxLength(100);
        builder.Property(lot => lot.Status).HasMaxLength(30);

        builder.HasOne(lot => lot.StoreProduct).WithMany().HasForeignKey(lot => lot.StoreProductId);
        builder.HasOne(lot => lot.Balance).WithOne().HasForeignKey<InventoryLotBalance>(balance => balance.InventoryLotId);

        builder.HasIndex(lot => new { lot.StoreProductId, lot.ExpiryDate });
        builder.HasIndex(lot => new { lot.Status, lot.ExpiryDate });
        builder.HasIndex(lot => lot.StoreProductId, "ux_inventory_lots_no_lot_bucket")
            .IsUnique()
            .HasFilter("lot_number IS NULL AND deleted_at IS NULL")
            .HasDatabaseName("ux_inventory_lots_no_lot_bucket");
    }
}
