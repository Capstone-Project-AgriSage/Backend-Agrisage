using AgriSage.Domain.Common.Exceptions;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Products;

public class ProductTests
{
    [Fact]
    public void Packagings_convert_directly_to_base_unit()
    {
        var (product, bottle, box) = CreateProductWithPackagings();

        Assert.Equal(2, product.Packagings.Count);
        Assert.True(bottle.IsBaseUnit);
        Assert.Equal(1, bottle.ConversionToBase);
        Assert.Equal(6, box.ConversionToBase);
        Assert.All(product.Packagings, p => Assert.Equal(product.Id, p.ProductId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-6)]
    public void Conversion_to_base_must_be_positive(long conversionToBase)
    {
        var product = CreateProduct();

        Assert.Throws<DomainException>(() =>
            product.AddPackaging(BoxUnitId, conversionToBase, isBaseUnit: false, isPurchaseUnit: true, isSaleUnit: true, ActiveStatus));
        Assert.Empty(product.Packagings);
    }

    [Fact]
    public void Base_packaging_must_have_conversion_of_one()
    {
        var product = CreateProduct();

        Assert.Throws<DomainException>(() =>
            product.AddPackaging(BoxUnitId, 6, isBaseUnit: true, isPurchaseUnit: false, isSaleUnit: true, ActiveStatus));
    }

    [Fact]
    public void Product_cannot_have_two_base_packagings()
    {
        var (product, _, _) = CreateProductWithPackagings();

        Assert.Throws<DomainException>(() =>
            product.AddPackaging(Guid.NewGuid(), 1, isBaseUnit: true, isPurchaseUnit: false, isSaleUnit: true, ActiveStatus));
    }

    [Fact]
    public void Product_cannot_have_two_packagings_for_the_same_unit()
    {
        var (product, _, _) = CreateProductWithPackagings();

        Assert.Throws<DomainException>(() =>
            product.AddPackaging(BoxUnitId, 12, isBaseUnit: false, isPurchaseUnit: true, isSaleUnit: false, ActiveStatus));
    }

    [Fact]
    public void Soft_deleted_base_packaging_no_longer_blocks_a_new_base_packaging()
    {
        var (product, bottle, _) = CreateProductWithPackagings();
        bottle.MarkDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var newBase = product.AddPackaging(Guid.NewGuid(), 1, isBaseUnit: true, isPurchaseUnit: false, isSaleUnit: true, ActiveStatus);

        Assert.True(newBase.IsBaseUnit);
    }
}
