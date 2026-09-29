using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 15: product_active_ingredients.
internal sealed class ProductActiveIngredientConfiguration : EntityConfiguration<ProductActiveIngredient>
{
    protected override string TableName => "product_active_ingredients";

    protected override void ConfigureEntity(EntityTypeBuilder<ProductActiveIngredient> builder)
    {
        builder.Property(link => link.Concentration).HasMaxLength(100);
        builder.Property(link => link.Note).HasMaxLength(500);

        builder.HasReference<ProductActiveIngredient, Product>(link => link.ProductId);
        builder.HasOne(link => link.ActiveIngredient).WithMany().HasForeignKey(link => link.ActiveIngredientId);

        builder.HasIndex(link => new { link.ProductId, link.ActiveIngredientId }).IsUnique();
    }
}
