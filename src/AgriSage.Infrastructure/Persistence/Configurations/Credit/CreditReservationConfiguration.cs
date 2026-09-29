using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Credit;

// Table 47: credit_reservations. One open (ACTIVE / PARTIALLY_CONSUMED) reservation per Order.
internal sealed class CreditReservationConfiguration : EntityConfiguration<CreditReservation>
{
    protected override string TableName => "credit_reservations";

    protected override void ConfigureEntity(EntityTypeBuilder<CreditReservation> builder)
    {
        builder.Property(reservation => reservation.AmountReserved).IsMoney();
        builder.Property(reservation => reservation.AmountConsumed).IsMoney().HasDbDefault(0m);
        builder.Property(reservation => reservation.AmountReleased).IsMoney().HasDbDefault(0m);
        builder.Property(reservation => reservation.Status).HasMaxLength(30);
        builder.Property(reservation => reservation.ReleaseReason).HasMaxLength(500);

        builder.Ignore(reservation => reservation.RemainingAmount);

        builder.HasReference<CreditReservation, Store>(reservation => reservation.StoreId);
        builder.HasReference<CreditReservation, FarmerCreditProfile>(reservation => reservation.FarmerCreditProfileId);
        builder.HasReference<CreditReservation, Order>(reservation => reservation.OrderId);
        builder.HasUserReference(reservation => reservation.ReservedBy);
        builder.HasUserReference(reservation => reservation.ReleasedBy);

        builder.HasIndex(reservation => new { reservation.FarmerCreditProfileId, reservation.Status });
        builder.HasIndex(reservation => reservation.OrderId);
        builder.HasIndex(reservation => reservation.OrderId, "ux_credit_reservations_open_order")
            .IsUnique()
            .HasFilter("status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL")
            .HasDatabaseName("ux_credit_reservations_open_order");

        builder.HasCheck("amount_reserved", "amount_reserved >= 0");
        builder.HasCheck("amount_consumed", "amount_consumed >= 0");
        builder.HasCheck("amount_released", "amount_released >= 0");
        builder.HasCheck("consumed_released_within_reserved", "amount_consumed + amount_released <= amount_reserved");
    }
}
