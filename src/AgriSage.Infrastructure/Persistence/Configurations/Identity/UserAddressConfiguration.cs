using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

// Table 4: user_addresses.
internal sealed class UserAddressConfiguration : EntityConfiguration<UserAddress>
{
    protected override string TableName => "user_addresses";

    protected override void ConfigureEntity(EntityTypeBuilder<UserAddress> builder)
    {
        builder.Property(address => address.RecipientName).HasMaxLength(150);
        builder.Property(address => address.RecipientPhone).HasMaxLength(20);
        builder.Property(address => address.AddressLine).HasMaxLength(500);
        builder.Property(address => address.Ward).HasMaxLength(150);
        builder.Property(address => address.District).HasMaxLength(150);
        builder.Property(address => address.Province).HasMaxLength(150);
        builder.Property(address => address.Latitude).IsCoordinate();
        builder.Property(address => address.Longitude).IsCoordinate();
        builder.Property(address => address.AddressType).HasMaxLength(20);
        builder.Property(address => address.IsDefault).HasDbDefault(false);

        builder.HasReference<UserAddress, User>(address => address.UserId);

        builder.HasIndex(address => address.UserId);

        // Maximum one active default address per user.
        builder.HasIndex(address => address.UserId, "ux_user_addresses_default")
            .IsUnique()
            .HasFilter("is_default AND deleted_at IS NULL")
            .HasDatabaseName("ux_user_addresses_default");
    }
}
