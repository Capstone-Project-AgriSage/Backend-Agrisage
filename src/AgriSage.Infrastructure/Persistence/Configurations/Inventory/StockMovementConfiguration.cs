using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 26: stock_movements. Aggregate root for stock_movement_items.
internal sealed class StockMovementConfiguration : EntityConfiguration<StockMovement>
{
    protected override string TableName => "stock_movements";

    protected override void ConfigureEntity(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(movement => movement.MovementNumber).HasMaxLength(50);
        builder.Property(movement => movement.MovementType).HasMaxLength(30);
        builder.Property(movement => movement.Status).HasMaxLength(20);
        builder.Property(movement => movement.ReasonCode).HasMaxLength(50);
        builder.Property(movement => movement.Reason).HasMaxLength(1000);

        builder.HasReference<StockMovement, Store>(movement => movement.StoreId);
        builder.HasReference<StockMovement, GoodsReceipt>(movement => movement.GoodsReceiptId);
        builder.HasReference<StockMovement, Order>(movement => movement.OrderId);
        builder.HasReference<StockMovement, Delivery>(movement => movement.DeliveryId);
        builder.HasReference<StockMovement, Stocktake>(movement => movement.StocktakeId);
        builder.HasReference<StockMovement, SalesReturn>(movement => movement.SalesReturnId);
        builder.HasReference<StockMovement, StockMovement>(movement => movement.ReversalOfMovementId);
        builder.HasUserReference(movement => movement.CreatedBy);
        builder.HasUserReference(movement => movement.PostedBy);
        builder.HasMany(movement => movement.Items).WithOne().HasForeignKey(item => item.StockMovementId);

        builder.HasIndex(movement => new { movement.StoreId, movement.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(movement => new { movement.MovementType, movement.OccurredAt }).IsDescending(false, true);
    }
}
