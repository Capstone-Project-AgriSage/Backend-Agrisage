using AgriSage.Domain.Features.Exchanges.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Exchanges;

internal sealed class SalesExchangeConfiguration : EntityConfiguration<SalesExchange>
{
    protected override string TableName => "sales_exchanges";
    protected override void ConfigureEntity(EntityTypeBuilder<SalesExchange> b)
    {
        b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.SettlementReference).HasMaxLength(200);
        b.Property(x => x.ReplacementAmount).IsMoney(); b.Property(x => x.Difference).IsMoney();
        b.HasReference<SalesExchange, Store>(x => x.StoreId);
        b.HasReference<SalesExchange, Order>(x => x.OrderId);
        b.HasReference<SalesExchange, Order>(x => x.ReplacementOrderId);
        b.HasReference<SalesExchange, SalesReturn>(x => x.SalesReturnId);
        b.HasReference<SalesExchange, FarmerProfile>(x => x.FarmerProfileId);
        b.HasUserReference(x => x.RequestedBy);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ExchangeId);
        b.HasIndex(x => new { x.StoreId, x.RequestedBy, x.RequestKey }).IsUnique();
        b.HasIndex(x => x.SalesReturnId).IsUnique();
        b.HasIndex(x => x.ReplacementOrderId).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.CreatedAt });
        b.HasCheck("amount", "replacement_amount >= 0");
        b.HasCheck("completion", "(status = 'COMPLETED' AND replacement_order_id IS NOT NULL AND completed_at IS NOT NULL AND settlement_reference IS NOT NULL AND difference IS NOT NULL) OR (status <> 'COMPLETED' AND replacement_order_id IS NULL AND completed_at IS NULL)");
        b.HasCheck("order", "replacement_order_id IS NULL OR replacement_order_id <> order_id");
    }
}
internal sealed class ExchangeItemConfiguration : EntityConfiguration<ExchangeItem>
{
    protected override string TableName => "exchange_items";
    protected override void ConfigureEntity(EntityTypeBuilder<ExchangeItem> b)
    {
        b.Property(x => x.ProductName).HasMaxLength(255); b.Property(x => x.PackagingName).HasMaxLength(100);
        b.Property(x => x.UnitPrice).IsMoney(); b.Property(x => x.LineTotal).IsMoney();
        b.HasReference<ExchangeItem, AgriSage.Domain.Features.Products.Entities.StoreProduct>(x => x.StoreProductId);
        b.HasReference<ExchangeItem, AgriSage.Domain.Features.Products.Entities.ProductPackaging>(x => x.ProductPackagingId);
        b.HasIndex(x => new { x.ExchangeId, x.StoreProductId, x.ProductPackagingId }).IsUnique();
        b.HasCheck("quantity", "quantity > 0 AND base_quantity > 0");
        b.HasCheck("amount", "unit_price >= 0 AND line_total = unit_price * quantity");
    }
}
