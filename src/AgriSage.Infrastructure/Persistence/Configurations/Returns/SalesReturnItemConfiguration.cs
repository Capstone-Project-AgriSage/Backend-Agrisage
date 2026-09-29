using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Returns;

// Table 53: sales_return_items. Exactly one fulfillment source (database design §35.10 / §35.14).
internal sealed class SalesReturnItemConfiguration : EntityConfiguration<SalesReturnItem>
{
    protected override string TableName => "sales_return_items";

    protected override void ConfigureEntity(EntityTypeBuilder<SalesReturnItem> builder)
    {
        builder.Property(item => item.SellingUnitPriceSnapshot).IsMoney();
        builder.Property(item => item.ReturnValue).IsMoney();
        builder.Property(item => item.OriginalCogsUnitCost).IsUnitCost();
        builder.Property(item => item.ReturnInventoryCostValue).IsUnitCost();
        builder.Property(item => item.ReasonCode).HasMaxLength(50);
        builder.Property(item => item.ConditionStatus).HasMaxLength(30);
        builder.Property(item => item.InventoryDisposition).HasMaxLength(30);
        builder.Property(item => item.InspectionNote).HasMaxLength(1000);

        builder.Ignore(item => item.IsInspected);

        builder.HasReference<SalesReturnItem, OrderItem>(item => item.OrderItemId);
        builder.HasReference<SalesReturnItem, DeliveryItem>(item => item.DeliveryItemId);
        builder.HasReference<SalesReturnItem, DeliveryItemLotAllocation>(item => item.DeliveryItemLotAllocationId);
        builder.HasReference<SalesReturnItem, StockMovementItem>(item => item.OriginalStockMovementItemId);
        builder.HasReference<SalesReturnItem, InventoryLot>(item => item.InventoryLotId);
        builder.HasReference<SalesReturnItem, StockMovement>(item => item.ReturnStockMovementId);
        builder.HasReference<SalesReturnItem, DebtTransaction>(item => item.DebtAdjustmentTransactionId);

        builder.HasCheck("returned_base_quantity", "returned_base_quantity > 0");
        builder.HasCheck("conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
        builder.HasCheck("selling_unit_price_snapshot", "selling_unit_price_snapshot >= 0");
        builder.HasCheck("return_value", "return_value >= 0");
        builder.HasCheck("original_cogs_unit_cost", "original_cogs_unit_cost IS NULL OR original_cogs_unit_cost >= 0");
        builder.HasCheck(
            "return_inventory_cost_value",
            "return_inventory_cost_value IS NULL OR return_inventory_cost_value >= 0");
        builder.HasCheck(
            "single_fulfillment_source",
            "(delivery_item_lot_allocation_id IS NULL) <> (original_stock_movement_item_id IS NULL)");
    }
}
