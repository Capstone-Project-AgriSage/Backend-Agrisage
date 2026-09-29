using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 14: active_ingredients.
internal sealed class ActiveIngredientConfiguration : EntityConfiguration<ActiveIngredient>
{
    protected override string TableName => "active_ingredients";

    protected override void ConfigureEntity(EntityTypeBuilder<ActiveIngredient> builder)
    {
        builder.Property(ingredient => ingredient.Code).HasMaxLength(50);
        builder.Property(ingredient => ingredient.Name).HasMaxLength(200);
        builder.Property(ingredient => ingredient.Description).IsText();
        builder.Property(ingredient => ingredient.IsActive).HasDbDefault(true);
    }
}
