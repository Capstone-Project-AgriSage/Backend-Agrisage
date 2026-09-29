using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.GoodsReceipts;

// Table 23: goods_receipt_items. inventory_lot_id stays NULL until the receipt is confirmed.
internal sealed class GoodsReceiptItemConfiguration : EntityConfiguration<GoodsReceiptItem>
{
    protected override string TableName => "goods_receipt_items";

    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptItem> builder)
    {
        builder.Property(item => item.PurchaseUnitCost).IsMoney();
        builder.Property(item => item.BaseUnitCost).IsUnitCost();
        builder.Property(item => item.LineTotalAmount).IsMoney();
        builder.Property(item => item.SupplierLotNumber).HasMaxLength(100);
        builder.Property(item => item.Note).HasMaxLength(500);

        builder.HasOne(item => item.StoreProduct).WithMany().HasForeignKey(item => item.StoreProductId);
        builder.HasOne(item => item.ProductPackaging).WithMany().HasForeignKey(item => item.ProductPackagingId);
        builder.HasReference<GoodsReceiptItem, InventoryLot>(item => item.InventoryLotId);

        builder.HasCheck("received_quantity", "received_quantity > 0");
        builder.HasCheck("conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
        builder.HasCheck("purchase_unit_cost", "purchase_unit_cost >= 0");
        builder.HasCheck("base_unit_cost", "base_unit_cost >= 0");
        builder.HasCheck("line_total_amount", "line_total_amount >= 0");
    }
}
