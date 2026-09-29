using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Orders;

// Table 34: orders. Aggregate root for order_items. `version` is mapped; its concurrency behavior is AGRI-13.
internal sealed class OrderConfiguration : EntityConfiguration<Order>
{
    protected override string TableName => "orders";

    protected override void ConfigureEntity(EntityTypeBuilder<Order> builder)
    {
        builder.Property(order => order.OrderNumber).HasMaxLength(50);
        builder.Property(order => order.Source).HasMaxLength(30);
        builder.Property(order => order.CustomerType).HasMaxLength(20);
        builder.Property(order => order.CustomerNameSnapshot).HasMaxLength(150);
        builder.Property(order => order.CustomerPhoneSnapshot).HasMaxLength(20);
        builder.Property(order => order.SettlementType).HasMaxLength(20);
        builder.Property(order => order.FulfillmentType).HasMaxLength(20);
        builder.Property(order => order.RecipientNameSnapshot).HasMaxLength(150);
        builder.Property(order => order.RecipientPhoneSnapshot).HasMaxLength(20);
        builder.Property(order => order.DeliveryAddressLine).HasMaxLength(500);
        builder.Property(order => order.DeliveryWard).HasMaxLength(150);
        builder.Property(order => order.DeliveryDistrict).HasMaxLength(150);
        builder.Property(order => order.DeliveryProvince).HasMaxLength(150);
        builder.Property(order => order.DeliveryLatitude).IsCoordinate();
        builder.Property(order => order.DeliveryLongitude).IsCoordinate();
        builder.Property(order => order.Status).HasMaxLength(40);
        builder.Property(order => order.SubtotalAmount).IsMoney().HasDbDefault(0m);
        builder.Property(order => order.TotalAmount).IsMoney().HasDbDefault(0m);
        builder.Property(order => order.Note).HasMaxLength(1000);
        builder.Property(order => order.CancelReason).HasMaxLength(1000);
        builder.Property(order => order.Version).HasDbDefault(0L);

        builder.HasReference<Order, Store>(order => order.StoreId);
        builder.HasOne(order => order.FarmerProfile).WithMany().HasForeignKey(order => order.FarmerProfileId);
        builder.HasReference<Order, CustomerGroup>(order => order.CustomerGroupIdSnapshot);
        builder.HasReference<Order, PriceList>(order => order.PriceListIdSnapshot);
        builder.HasReference<Order, UserAddress>(order => order.SourceAddressId);
        builder.HasUserReference(order => order.CreatedBy);
        builder.HasUserReference(order => order.ConfirmedBy);
        builder.HasUserReference(order => order.PickupCompletedBy);
        builder.HasUserReference(order => order.CancelledBy);
        builder.HasMany(order => order.Items).WithOne().HasForeignKey(item => item.OrderId);

        builder.HasIndex(order => new { order.StoreId, order.OrderNumber }).IsUnique();
        builder.HasIndex(order => new { order.StoreId, order.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(order => new { order.FarmerProfileId, order.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(order => new { order.Status, order.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(order => new { order.FulfillmentType, order.Status });
        builder.HasIndex(order => new { order.SettlementType, order.Status });

        builder.HasCheck("subtotal_amount", "subtotal_amount >= 0");
        builder.HasCheck("total_amount", "total_amount >= 0");
        builder.HasCheck("credit_term_days_snapshot", "credit_term_days_snapshot IS NULL OR credit_term_days_snapshot >= 0");
        // REGISTERED ⇔ Farmer Profile; WALK_IN ⇒ FULL_PAYMENT (so CREDIT ⇒ REGISTERED).
        builder.HasCheck(
            "customer_settlement",
            "(customer_type = 'REGISTERED' AND farmer_profile_id IS NOT NULL) "
            + "OR (customer_type = 'WALK_IN' AND farmer_profile_id IS NULL AND settlement_type = 'FULL_PAYMENT')");
    }
}
