using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Orders;

// Table 32: carts. Only one ACTIVE cart per Farmer per Store.
internal sealed class CartConfiguration : EntityConfiguration<Cart>
{
    protected override string TableName => "carts";

    protected override void ConfigureEntity(EntityTypeBuilder<Cart> builder)
    {
        builder.Property(cart => cart.Status).HasMaxLength(20);

        builder.HasReference<Cart, Store>(cart => cart.StoreId);
        builder.HasReference<Cart, FarmerProfile>(cart => cart.FarmerProfileId);
        builder.HasReference<Cart, Order>(cart => cart.ConvertedOrderId);
        builder.HasMany(cart => cart.Items).WithOne().HasForeignKey(item => item.CartId);

        builder.HasIndex(cart => new { cart.StoreId, cart.FarmerProfileId })
            .IsUnique()
            .HasFilter("status = 'ACTIVE' AND deleted_at IS NULL");
    }
}
