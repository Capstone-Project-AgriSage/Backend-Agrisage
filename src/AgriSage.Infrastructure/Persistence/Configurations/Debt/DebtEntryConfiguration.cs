using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// Table 49: debt_entries. Aggregate root for debt_entry_actions.
internal sealed class DebtEntryConfiguration : EntityConfiguration<DebtEntry>
{
    protected override string TableName => "debt_entries";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtEntry> builder)
    {
        builder.Property(entry => entry.EntryNumber).HasMaxLength(50);
        builder.Property(entry => entry.SourceType).HasMaxLength(30);
        builder.Property(entry => entry.FulfillmentValue).IsMoney();
        builder.Property(entry => entry.PrepaymentAppliedAmount).IsMoney().HasDbDefault(0m);
        builder.Property(entry => entry.OriginalAmount).IsMoney();
        builder.Property(entry => entry.OutstandingAmount).IsMoney();
        builder.Property(entry => entry.Status).HasMaxLength(30);

        builder.HasReference<DebtEntry, DebtAccount>(entry => entry.DebtAccountId);
        builder.HasReference<DebtEntry, Order>(entry => entry.OrderId);
        builder.HasReference<DebtEntry, Delivery>(entry => entry.DeliveryId);
        builder.HasReference<DebtEntry, DeliveryAttempt>(entry => entry.DeliveryAttemptId);
        builder.HasReference<DebtEntry, StockMovement>(entry => entry.SourceStockMovementId);
        builder.HasUserReference(entry => entry.CreatedBy);
        builder.HasMany(entry => entry.Actions).WithOne().HasForeignKey(action => action.DebtEntryId);

        builder.HasIndex(entry => entry.EntryNumber).IsUnique();
        builder.HasIndex(entry => new { entry.DebtAccountId, entry.Status, entry.DueDate });
        builder.HasIndex(entry => new { entry.Status, entry.DueDate });

        builder.HasCheck("fulfillment_value", "fulfillment_value >= 0");
        builder.HasCheck("prepayment_applied_amount", "prepayment_applied_amount >= 0");
        builder.HasCheck("original_amount", "original_amount >= 0");
        builder.HasCheck("outstanding_amount", "outstanding_amount >= 0");
        builder.HasCheck("prepayment_within_fulfillment", "prepayment_applied_amount <= fulfillment_value");
        builder.HasCheck("original_amount_formula", "original_amount = fulfillment_value - prepayment_applied_amount");
        builder.HasCheck("outstanding_within_original", "outstanding_amount <= original_amount");
        builder.HasCheck(
            "source_references",
            "(source_type = 'DELIVERY' AND order_id IS NOT NULL AND delivery_id IS NOT NULL "
            + "AND source_stock_movement_id IS NOT NULL) "
            + "OR (source_type = 'PICKUP' AND order_id IS NOT NULL AND source_stock_movement_id IS NOT NULL) "
            + "OR source_type = 'MANUAL_ADJUSTMENT'");
    }
}
