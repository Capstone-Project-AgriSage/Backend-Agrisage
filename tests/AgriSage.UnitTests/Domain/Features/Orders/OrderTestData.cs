using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Orders;

// Shared order builders: a registered Farmer order for 2 boxes of 6 bottles (12 base units) at 120,000 per box.
internal static class OrderTestData
{
    public const long BoxQuantity = 2;
    public const long BaseQuantity = 12;
    public const decimal BoxPrice = 120_000m;

    public static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    public static readonly Guid StaffId = Guid.NewGuid();
    public static readonly DeliveryAddress Address = new("Nguyen Van A", "0900000000", "12 Duong 3/2", "Can Tho");

    public static Order CreateOrder(
        FulfillmentType fulfillmentType = FulfillmentType.Pickup,
        SettlementType settlementType = SettlementType.FullPayment) =>
        new(
            Guid.NewGuid(),
            "SO-0001",
            OrderSource.Counter,
            CustomerType.Registered,
            StaffId,
            "Nguyen Van A",
            settlementType,
            fulfillmentType,
            farmerProfileId: Guid.NewGuid(),
            deliveryAddress: fulfillmentType == FulfillmentType.Delivery ? Address : null);

    public static (Order Order, OrderItem Item) CreateOrderWithItem(
        FulfillmentType fulfillmentType = FulfillmentType.Pickup,
        SettlementType settlementType = SettlementType.FullPayment)
    {
        var order = CreateOrder(fulfillmentType, settlementType);
        var (product, _, box) = CreateProductWithPackagings();
        var item = order.AddItem(
            CreateStoreProduct(product),
            box,
            product.Sku,
            product.Name,
            "Box of 6",
            BoxQuantity,
            BoxPrice);

        return (order, item);
    }

    public static (Order Order, OrderItem Item) CreateConfirmedOrderWithItem(
        FulfillmentType fulfillmentType = FulfillmentType.Pickup)
    {
        var (order, item) = CreateOrderWithItem(fulfillmentType);
        order.Confirm(StaffId, Now);
        return (order, item);
    }
}
