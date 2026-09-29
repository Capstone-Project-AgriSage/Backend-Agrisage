using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 10: brands. Recommended unique active brand name.
internal sealed class BrandConfiguration : EntityConfiguration<Brand>
{
    protected override string TableName => "brands";

    protected override void ConfigureEntity(EntityTypeBuilder<Brand> builder)
    {
        builder.Property(brand => brand.Code).HasMaxLength(50);
        builder.Property(brand => brand.Name).HasMaxLength(150);
        builder.Property(brand => brand.Description).HasMaxLength(500);
        builder.Property(brand => brand.LogoUrl).HasMaxLength(1000);
        builder.Property(brand => brand.IsActive).HasDbDefault(true);

        builder.HasIndex(brand => brand.Name).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
