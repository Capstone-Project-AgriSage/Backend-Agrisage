using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Suppliers.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Suppliers;

// Table 21: suppliers.
internal sealed class SupplierConfiguration : EntityConfiguration<Supplier>
{
    protected override string TableName => "suppliers";

    protected override void ConfigureEntity(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(supplier => supplier.Code).HasMaxLength(50);
        builder.Property(supplier => supplier.Name).HasMaxLength(255);
        builder.Property(supplier => supplier.TaxCode).HasMaxLength(50);
        builder.Property(supplier => supplier.PhoneNumber).HasMaxLength(20);
        builder.Property(supplier => supplier.Email).HasMaxLength(255);
        builder.Property(supplier => supplier.ContactPerson).HasMaxLength(150);
        builder.Property(supplier => supplier.AddressLine).HasMaxLength(500);
        builder.Property(supplier => supplier.Ward).HasMaxLength(150);
        builder.Property(supplier => supplier.District).HasMaxLength(150);
        builder.Property(supplier => supplier.Province).HasMaxLength(150);
        builder.Property(supplier => supplier.Note).HasMaxLength(1000);
        builder.Property(supplier => supplier.IsActive).HasDbDefault(true);

        builder.HasReference<Supplier, Store>(supplier => supplier.StoreId);

        builder.HasIndex(supplier => new { supplier.StoreId, supplier.Code })
            .IsUnique()
            .HasFilter("code IS NOT NULL AND deleted_at IS NULL");
        builder.HasIndex(supplier => new { supplier.StoreId, supplier.Name });
    }
}
