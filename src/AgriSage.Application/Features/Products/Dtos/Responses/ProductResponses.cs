namespace AgriSage.Application.Features.Products.Dtos.Responses;

public sealed record PackagingResponse(
    Guid Id,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    string? PackagingName,
    long ConversionToBase,
    bool IsBaseUnit,
    bool IsPurchaseUnit,
    bool IsSaleUnit,
    string? Barcode,
    string Status);

public sealed record ProductIngredientResponse(Guid ActiveIngredientId, string Name, string? Concentration, string? Note);

public sealed record ProductStoreInfo(Guid StoreProductId, bool IsSellable, bool IsActive);

public sealed record ProductListItem(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    string CategoryName,
    Guid? BrandId,
    string? BrandName,
    string? ImageUrl,
    string Status);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    string CategoryName,
    Guid? BrandId,
    string? BrandName,
    string? Description,
    string? UsageInstructions,
    bool RequiresLotTracking,
    bool RequiresExpiryDate,
    string? ImageUrl,
    string Status,
    IReadOnlyList<PackagingResponse> Packagings,
    IReadOnlyList<ProductIngredientResponse> Ingredients,
    ProductStoreInfo? Store);

public sealed record StoreProductResponse(
    Guid Id,
    Guid ProductId,
    string Sku,
    string Name,
    string? ImageUrl,
    string ProductStatus,
    string? StoreSku,
    long? MinStockLevelBase,
    bool IsSellable,
    bool IsActive);
