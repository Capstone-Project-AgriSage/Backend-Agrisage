using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Products.Dtos.Requests;

// Conversion to base and the base flag cannot change after creation (transactions snapshot the conversion).
public sealed record PackagingRequest(
    Guid UnitId,
    long ConversionToBase,
    bool IsBaseUnit,
    bool IsPurchaseUnit,
    bool IsSaleUnit,
    string? PackagingName = null,
    string? Barcode = null);

// Packagings must include exactly one base packaging (conversion 1). SKU, lot tracking and expiry flags are fixed.
public sealed record CreateProductRequest(
    string Sku,
    string Name,
    Guid CategoryId,
    IReadOnlyList<PackagingRequest> Packagings,
    Guid? BrandId = null,
    string? Description = null,
    string? UsageInstructions = null,
    string? ImageUrl = null,
    bool RequiresLotTracking = true,
    bool RequiresExpiryDate = true);

public sealed record UpdateProductRequest(
    string Name,
    Guid CategoryId,
    Guid? BrandId = null,
    string? Description = null,
    string? UsageInstructions = null,
    string? ImageUrl = null);

// ACTIVE, INACTIVE or DISCONTINUED.
public sealed record ChangeProductStatusRequest(string Status);

// Status is ACTIVE or INACTIVE.
public sealed record UpdatePackagingRequest(
    bool IsPurchaseUnit,
    bool IsSaleUnit,
    string Status,
    string? PackagingName = null,
    string? Barcode = null);

public sealed record ProductIngredientRequest(Guid ActiveIngredientId, string? Concentration = null, string? Note = null);

// Replaces the whole ingredient list of the product.
public sealed record SetProductIngredientsRequest(IReadOnlyList<ProductIngredientRequest> Ingredients);

public sealed record ProductListRequest : PaginationRequest
{
    public Guid? CategoryId { get; init; }

    public Guid? BrandId { get; init; }

    public string? Status { get; init; }

    public string? Search { get; init; }
}

public sealed record CreateStoreProductRequest(Guid ProductId, string? StoreSku = null, long? MinStockLevelBase = null);

public sealed record UpdateStoreProductRequest(string? StoreSku, long? MinStockLevelBase);

public sealed record StoreProductListRequest : PaginationRequest
{
    public bool? IsActive { get; init; }

    public bool? IsSellable { get; init; }

    public string? Search { get; init; }
}

public sealed record UnitListRequest : PaginationRequest;

// Public catalog (no sign-in): only active, sellable products of the store.
public sealed record CatalogProductListRequest : PaginationRequest
{
    // Includes the sub-categories of the category.
    public Guid? CategoryId { get; init; }

    public Guid? BrandId { get; init; }

    public string? Search { get; init; }
}
