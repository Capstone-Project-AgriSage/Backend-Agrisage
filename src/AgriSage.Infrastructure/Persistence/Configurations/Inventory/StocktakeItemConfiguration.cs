using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 29: stocktake_items.
internal sealed class StocktakeItemConfiguration : EntityConfiguration<StocktakeItem>
{
    protected override string TableName => "stocktake_items";

    protected override void ConfigureEntity(EntityTypeBuilder<StocktakeItem> builder)
    {
        builder.Property(item => item.UnitCostSnapshot).IsUnitCost();
        builder.Property(item => item.DifferenceCostValue).IsUnitCost();
        builder.Property(item => item.ReasonCode).HasMaxLength(50);
        builder.Property(item => item.Note).HasMaxLength(500);

        builder.Ignore(item => item.IsCounted);

        builder.HasReference<StocktakeItem, InventoryLot>(item => item.InventoryLotId);
        builder.HasUserReference(item => item.CountedBy);

        builder.HasIndex(item => new { item.StocktakeId, item.InventoryLotId }).IsUnique();

        builder.HasCheck("system_quantity_snapshot", "system_quantity_snapshot >= 0");
        builder.HasCheck("counted_quantity", "counted_quantity IS NULL OR counted_quantity >= 0");
    }
}
