using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 9: categories (self-referencing parent).
internal sealed class CategoryConfiguration : EntityConfiguration<Category>
{
    protected override string TableName => "categories";

    protected override void ConfigureEntity(EntityTypeBuilder<Category> builder)
    {
        builder.Property(category => category.Code).HasMaxLength(50);
        builder.Property(category => category.Name).HasMaxLength(150);
        builder.Property(category => category.Description).HasMaxLength(500);
        builder.Property(category => category.DisplayOrder).HasDbDefault(0);
        builder.Property(category => category.IsActive).HasDbDefault(true);

        builder.HasOne(category => category.Parent).WithMany().HasForeignKey(category => category.ParentId);
    }
}
