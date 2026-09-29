using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Orders;

public class CartTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_product_and_packaging_keeps_a_single_line()
    {
        var cart = new Cart(Guid.NewGuid(), Guid.NewGuid());
        var (product, _, box) = CreateProductWithPackagings();
        var storeProduct = CreateStoreProduct(product);

        cart.SetItemQuantity(storeProduct, box, 2);
        var line = cart.SetItemQuantity(storeProduct, box, 5);

        Assert.Single(cart.Items);
        Assert.Equal(5, line.Quantity);
    }

    [Fact]
    public void Cart_rejects_packaging_of_another_product()
    {
        var cart = new Cart(Guid.NewGuid(), Guid.NewGuid());
        var (product, _, _) = CreateProductWithPackagings("SKU-A");
        var (_, _, otherBox) = CreateProductWithPackagings("SKU-B");

        Assert.Throws<DomainException>(() => cart.SetItemQuantity(CreateStoreProduct(product), otherBox, 1));
    }

    [Fact]
    public void Converted_cart_can_no_longer_change()
    {
        var cart = new Cart(Guid.NewGuid(), Guid.NewGuid());
        var (product, _, box) = CreateProductWithPackagings();
        var storeProduct = CreateStoreProduct(product);
        var line = cart.SetItemQuantity(storeProduct, box, 2);
        var orderId = Guid.NewGuid();

        cart.MarkConverted(orderId, Now);

        Assert.Equal(CartStatus.Converted, cart.Status);
        Assert.Equal(orderId, cart.ConvertedOrderId);
        Assert.Throws<DomainException>(() => cart.SetItemQuantity(storeProduct, box, 3));
        Assert.Throws<DomainException>(() => cart.RemoveItem(line.Id, null, Now));
        Assert.Throws<DomainException>(cart.MarkAbandoned);
    }
}
