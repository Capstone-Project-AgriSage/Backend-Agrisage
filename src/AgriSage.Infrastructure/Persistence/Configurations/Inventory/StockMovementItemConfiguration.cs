using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 27: stock_movement_items.
internal sealed class StockMovementItemConfiguration : EntityConfiguration<StockMovementItem>
{
    protected override string TableName => "stock_movement_items";

    protected override void ConfigureEntity(EntityTypeBuilder<StockMovementItem> builder)
    {
        builder.Property(item => item.UnitCostSnapshot).IsUnitCost();
        builder.Property(item => item.TotalCostSnapshot).IsUnitCost();
        builder.Property(item => item.TotalCostValueAfter).IsUnitCost();
        builder.Property(item => item.Note).HasMaxLength(500);

        builder.HasReference<StockMovementItem, InventoryLot>(item => item.InventoryLotId);

        builder.HasCheck("quantity_delta_base", "quantity_delta_base <> 0");
        builder.HasCheck("unit_cost_snapshot", "unit_cost_snapshot >= 0");
        builder.HasCheck("total_cost_snapshot", "total_cost_snapshot >= 0");
    }
}
