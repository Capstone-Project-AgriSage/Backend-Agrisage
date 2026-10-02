namespace AgriSage.Application.Features.Products.Dtos.Responses;

// Public catalog: no stock, minimum stock, store SKU, lot/expiry flags, internal statuses or audit data.
// The id is the store-product id (what an order will reference). Prices are added with the price lists.
public sealed record PublicBrand(Guid Id, string Name, string? LogoUrl);

public sealed record PublicPackaging(
    Guid Id,
    string UnitName,
    string? Symbol,
    string? PackagingName,
    long ConversionToBase,
    bool IsBaseUnit,
    string? Barcode);

public sealed record PublicIngredient(string Name, string? Concentration);

public sealed record PublicProductListItem(
    Guid Id,
    string Sku,
    string Name,
    string? ImageUrl,
    Guid CategoryId,
    string CategoryName,
    string? BrandName);

public sealed record PublicProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    string? UsageInstructions,
    string? ImageUrl,
    Guid CategoryId,
    string CategoryName,
    Guid? BrandId,
    string? BrandName,
    IReadOnlyList<PublicPackaging> Packagings,
    IReadOnlyList<PublicIngredient> Ingredients);
