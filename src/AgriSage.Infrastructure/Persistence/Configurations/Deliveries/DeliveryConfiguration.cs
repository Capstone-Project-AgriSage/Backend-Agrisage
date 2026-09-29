using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// Table 38: deliveries. Aggregate root for items, lot allocations, attempts and attempt items.
internal sealed class DeliveryConfiguration : EntityConfiguration<Delivery>
{
    protected override string TableName => "deliveries";

    protected override void ConfigureEntity(EntityTypeBuilder<Delivery> builder)
    {
        builder.Property(delivery => delivery.DeliveryNumber).HasMaxLength(50);
        builder.Property(delivery => delivery.RecipientNameSnapshot).HasMaxLength(150);
        builder.Property(delivery => delivery.RecipientPhoneSnapshot).HasMaxLength(20);
        builder.Property(delivery => delivery.AddressLineSnapshot).HasMaxLength(500);
        builder.Property(delivery => delivery.WardSnapshot).HasMaxLength(150);
        builder.Property(delivery => delivery.DistrictSnapshot).HasMaxLength(150);
        builder.Property(delivery => delivery.ProvinceSnapshot).HasMaxLength(150);
        builder.Property(delivery => delivery.LatitudeSnapshot).IsCoordinate();
        builder.Property(delivery => delivery.LongitudeSnapshot).IsCoordinate();
        builder.Property(delivery => delivery.Status).HasMaxLength(30);
        builder.Property(delivery => delivery.Note).HasMaxLength(1000);
        builder.Property(delivery => delivery.CancelReason).HasMaxLength(1000);

        builder.Ignore(delivery => delivery.RemainingBaseQuantity);

        builder.HasReference<Delivery, Store>(delivery => delivery.StoreId);
        builder.HasReference<Delivery, Order>(delivery => delivery.OrderId);
        builder.HasOne(delivery => delivery.AssignedToMember).WithMany().HasForeignKey(delivery => delivery.AssignedToMemberId);
        builder.HasUserReference(delivery => delivery.CreatedBy);
        builder.HasUserReference(delivery => delivery.CancelledBy);
        builder.HasMany(delivery => delivery.Items).WithOne().HasForeignKey(item => item.DeliveryId);
        builder.HasMany(delivery => delivery.Attempts).WithOne().HasForeignKey(attempt => attempt.DeliveryId);

        builder.HasIndex(delivery => new { delivery.StoreId, delivery.DeliveryNumber }).IsUnique();
        builder.HasIndex(delivery => new { delivery.AssignedToMemberId, delivery.Status, delivery.ScheduledAt });
        builder.HasIndex(delivery => new { delivery.Status, delivery.ScheduledAt });
    }
}
