using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 11: products. Aggregate root for product_packagings.
internal sealed class ProductConfiguration : EntityConfiguration<Product>
{
    protected override string TableName => "products";

    protected override void ConfigureEntity(EntityTypeBuilder<Product> builder)
    {
        builder.Property(product => product.Sku).HasMaxLength(50);
        builder.Property(product => product.Name).HasMaxLength(255);
        builder.Property(product => product.Description).IsText();
        builder.Property(product => product.UsageInstructions).IsText();
        builder.Property(product => product.RequiresLotTracking).HasDbDefault(true);
        builder.Property(product => product.RequiresExpiryDate).HasDbDefault(true);
        builder.Property(product => product.ImageUrl).HasMaxLength(1000);
        builder.Property(product => product.Status).HasMaxLength(20);

        builder.HasOne(product => product.Category).WithMany().HasForeignKey(product => product.CategoryId);
        builder.HasOne(product => product.Brand).WithMany().HasForeignKey(product => product.BrandId);
        builder.HasMany(product => product.Packagings).WithOne().HasForeignKey(packaging => packaging.ProductId);

        builder.HasIndex(product => product.Sku).IsUnique();
        builder.HasIndex(product => product.Status);
    }
}
