namespace AgriSage.Application.Features.Products.Dtos.Responses;

// Public catalog: no stock, minimum stock, store SKU, lot/expiry flags, internal statuses or audit data.
// The id is the store-product id (what an order will reference). Prices come from the ACTIVE walk-in default price
// list (FLOW_1 §3); null when there is none or the packaging is not priced.
public sealed record PublicBrand(Guid Id, string Name, string? LogoUrl);

// Price: selling price of a sale packaging (null for a base packaging that is not sold).
public sealed record PublicPackaging(
    Guid Id,
    string UnitName,
    string? Symbol,
    string? PackagingName,
    long ConversionToBase,
    bool IsBaseUnit,
    string? Barcode,
    decimal? Price);

public sealed record PublicIngredient(string Name, string? Concentration);

// FromPrice: lowest price among the product's ACTIVE sale packagings.
public sealed record PublicProductListItem(
    Guid Id,
    string Sku,
    string Name,
    string? ImageUrl,
    Guid CategoryId,
    string CategoryName,
    string? BrandName,
    decimal? FromPrice);

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
