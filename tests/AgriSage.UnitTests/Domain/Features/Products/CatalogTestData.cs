using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.UnitTests.Domain.Features.Products;

// Shared catalog builders for domain tests: a product sold by bottle (base) and box of 6.
internal static class CatalogTestData
{
    public const string ActiveStatus = "ACTIVE";

    public static readonly Guid BottleUnitId = Guid.NewGuid();
    public static readonly Guid BoxUnitId = Guid.NewGuid();

    public static Product CreateProduct(string sku = "SKU-001") =>
        new(categoryId: Guid.NewGuid(), sku, name: $"Product {sku}");

    public static (Product Product, ProductPackaging Bottle, ProductPackaging Box) CreateProductWithPackagings(
        string sku = "SKU-001")
    {
        var product = CreateProduct(sku);
        var bottle = product.AddPackaging(BottleUnitId, 1, isBaseUnit: true, isPurchaseUnit: false, isSaleUnit: true, ActiveStatus);
        var box = product.AddPackaging(BoxUnitId, 6, isBaseUnit: false, isPurchaseUnit: true, isSaleUnit: true, ActiveStatus);

        return (product, bottle, box);
    }

    public static StoreProduct CreateStoreProduct(Product product) => new(storeId: Guid.NewGuid(), product.Id);
}
