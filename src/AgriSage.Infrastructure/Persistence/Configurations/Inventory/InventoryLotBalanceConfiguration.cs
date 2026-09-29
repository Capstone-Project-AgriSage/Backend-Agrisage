using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 25: inventory_lot_balances. `version` is mapped; its concurrency-token behavior is AGRI-13.
internal sealed class InventoryLotBalanceConfiguration : EntityConfiguration<InventoryLotBalance>
{
    protected override string TableName => "inventory_lot_balances";

    protected override void ConfigureEntity(EntityTypeBuilder<InventoryLotBalance> builder)
    {
        builder.Property(balance => balance.QuantityOnHand).HasDbDefault(0L);
        builder.Property(balance => balance.QuantityReserved).HasDbDefault(0L);
        builder.Property(balance => balance.TotalCostValue).IsUnitCost().HasDbDefault(0m);
        builder.Property(balance => balance.Version).HasDbDefault(0L);

        builder.Ignore(balance => balance.AvailableQuantity);
        builder.Ignore(balance => balance.AverageUnitCost);

        builder.HasIndex(balance => balance.InventoryLotId).IsUnique();

        builder.HasCheck("quantity_on_hand", "quantity_on_hand >= 0");
        builder.HasCheck("quantity_reserved", "quantity_reserved >= 0");
        builder.HasCheck("reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
        builder.HasCheck("total_cost_value", "total_cost_value >= 0");
        builder.HasCheck("zero_quantity_zero_cost", "quantity_on_hand > 0 OR total_cost_value = 0");
    }
}
