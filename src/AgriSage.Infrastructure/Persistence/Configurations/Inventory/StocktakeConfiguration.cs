using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// Table 28: stocktakes. Aggregate root for stocktake_items.
internal sealed class StocktakeConfiguration : EntityConfiguration<Stocktake>
{
    protected override string TableName => "stocktakes";

    protected override void ConfigureEntity(EntityTypeBuilder<Stocktake> builder)
    {
        builder.Property(stocktake => stocktake.StocktakeNumber).HasMaxLength(50);
        builder.Property(stocktake => stocktake.Status).HasMaxLength(30);
        builder.Property(stocktake => stocktake.Note).HasMaxLength(1000);

        builder.HasReference<Stocktake, Store>(stocktake => stocktake.StoreId);
        builder.HasUserReference(stocktake => stocktake.StartedBy);
        builder.HasUserReference(stocktake => stocktake.CompletedBy);
        builder.HasUserReference(stocktake => stocktake.CreatedBy);
        builder.HasMany(stocktake => stocktake.Items).WithOne().HasForeignKey(item => item.StocktakeId);

        builder.HasIndex(stocktake => new { stocktake.StoreId, stocktake.StocktakeNumber }).IsUnique();
        builder.HasIndex(stocktake => new { stocktake.StoreId, stocktake.Status });
        builder.HasIndex(stocktake => new { stocktake.StoreId, stocktake.CreatedAt }).IsDescending(false, true);
    }
}
