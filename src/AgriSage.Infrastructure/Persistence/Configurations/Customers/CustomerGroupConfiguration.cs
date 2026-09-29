using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Customers;

// Table 7: customer_groups.
internal sealed class CustomerGroupConfiguration : EntityConfiguration<CustomerGroup>
{
    protected override string TableName => "customer_groups";

    protected override void ConfigureEntity(EntityTypeBuilder<CustomerGroup> builder)
    {
        builder.Property(group => group.Code).HasMaxLength(30);
        builder.Property(group => group.Name).HasMaxLength(100);
        builder.Property(group => group.Description).HasMaxLength(500);
        builder.Property(group => group.Priority).HasDbDefault(0);
        builder.Property(group => group.IsDefault).HasDbDefault(false);
        builder.Property(group => group.IsActive).HasDbDefault(true);

        builder.HasReference<CustomerGroup, Store>(group => group.StoreId);

        builder.HasIndex(group => new { group.StoreId, group.Code }).IsUnique();

        // Only one active default group per Store.
        builder.HasIndex(group => group.StoreId, "ux_customer_groups_default")
            .IsUnique()
            .HasFilter("is_default AND is_active AND deleted_at IS NULL")
            .HasDatabaseName("ux_customer_groups_default");
    }
}
