using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Orders.Entities;

namespace AgriSage.Domain.Features.Exchanges.Entities;

public sealed class ExchangeItem : SoftDeletableChildEntity
{
    private ExchangeItem() { }
    internal ExchangeItem(Guid exchangeId, OrderItem item)
    {
        ExchangeId = exchangeId; StoreProductId = item.StoreProductId; ProductPackagingId = item.ProductPackagingId;
        Quantity = item.Quantity; BaseQuantity = item.BaseQuantity; UnitPrice = item.UnitPrice;
        ProductName = item.ProductNameSnapshot; PackagingName = item.PackagingNameSnapshot; LineTotal = item.LineTotalAmount;
    }
    public Guid ExchangeId { get; private set; }
    public Guid StoreProductId { get; private set; }
    public Guid ProductPackagingId { get; private set; }
    public long Quantity { get; private set; }
    public long BaseQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public string ProductName { get; private set; } = null!;
    public string PackagingName { get; private set; } = null!;
}
