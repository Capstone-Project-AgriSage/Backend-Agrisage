using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Orders;

// Table 33: cart_items. Not a price snapshot.
internal sealed class CartItemConfiguration : EntityConfiguration<CartItem>
{
    protected override string TableName => "cart_items";

    protected override void ConfigureEntity(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasOne(item => item.StoreProduct).WithMany().HasForeignKey(item => item.StoreProductId);
        builder.HasOne(item => item.ProductPackaging).WithMany().HasForeignKey(item => item.ProductPackagingId);

        builder.HasIndex(item => item.CartId);
        builder.HasIndex(item => new { item.CartId, item.StoreProductId, item.ProductPackagingId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasCheck("quantity", "quantity > 0");
    }
}
