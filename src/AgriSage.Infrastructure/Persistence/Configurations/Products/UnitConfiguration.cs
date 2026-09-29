using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Products;

// Table 12: units.
internal sealed class UnitConfiguration : EntityConfiguration<Unit>
{
    protected override string TableName => "units";

    protected override void ConfigureEntity(EntityTypeBuilder<Unit> builder)
    {
        builder.Property(unit => unit.Code).HasMaxLength(30);
        builder.Property(unit => unit.Name).HasMaxLength(100);
        builder.Property(unit => unit.Symbol).HasMaxLength(20);
        builder.Property(unit => unit.IsActive).HasDbDefault(true);

        builder.HasIndex(unit => unit.Code).IsUnique();
    }
}
