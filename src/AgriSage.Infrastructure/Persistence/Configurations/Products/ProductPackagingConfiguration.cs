using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 13: product_packagings. Status stays a string (values not defined by the database design).
internal sealed class ProductPackagingConfiguration : EntityConfiguration<ProductPackaging>
{
    protected override string TableName => "product_packagings";

    protected override void ConfigureEntity(EntityTypeBuilder<ProductPackaging> builder)
    {
        builder.Property(packaging => packaging.PackagingName).HasMaxLength(150);
        builder.Property(packaging => packaging.IsBaseUnit).HasDbDefault(false);
        builder.Property(packaging => packaging.IsPurchaseUnit).HasDbDefault(false);
        builder.Property(packaging => packaging.IsSaleUnit).HasDbDefault(false);
        builder.Property(packaging => packaging.Barcode).HasMaxLength(100);
        builder.Property(packaging => packaging.Status).HasMaxLength(20);

        builder.HasOne(packaging => packaging.Unit).WithMany().HasForeignKey(packaging => packaging.UnitId);

        builder.HasIndex(packaging => new { packaging.ProductId, packaging.Status });
        builder.HasIndex(packaging => new { packaging.ProductId, packaging.UnitId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(packaging => packaging.ProductId, "ux_product_packagings_base_unit")
            .IsUnique()
            .HasFilter("is_base_unit AND deleted_at IS NULL")
            .HasDatabaseName("ux_product_packagings_base_unit");
        builder.HasIndex(packaging => packaging.Barcode)
            .IsUnique()
            .HasFilter("barcode IS NOT NULL AND deleted_at IS NULL");

        builder.HasCheck("conversion_to_base", "conversion_to_base > 0");
        builder.HasCheck("base_unit_conversion", "NOT is_base_unit OR conversion_to_base = 1");
    }
}
