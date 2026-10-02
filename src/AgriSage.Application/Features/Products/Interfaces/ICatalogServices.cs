using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;

namespace AgriSage.Application.Features.Products.Interfaces;

public interface ICategoryService
{
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);

    Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken);

    Task<CategoryResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<CategoryResponse>> ListAsync(CategoryListRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryTreeNode>> GetTreeAsync(bool activeOnly, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);

    // Soft delete; refused while the category has sub-categories or products.
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IBrandService
{
    Task<BrandResponse> CreateAsync(BrandRequest request, CancellationToken cancellationToken);

    Task<BrandResponse> UpdateAsync(Guid id, BrandRequest request, CancellationToken cancellationToken);

    Task<BrandResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<BrandResponse>> ListAsync(BrandListRequest request, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);

    // Soft delete; refused while products use the brand.
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IActiveIngredientService
{
    Task<ActiveIngredientResponse> CreateAsync(ActiveIngredientRequest request, CancellationToken cancellationToken);

    Task<ActiveIngredientResponse> UpdateAsync(Guid id, ActiveIngredientRequest request, CancellationToken cancellationToken);

    Task<ActiveIngredientResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ActiveIngredientResponse>> ListAsync(ActiveIngredientListRequest request, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);

    // Soft delete; refused while products use the ingredient.
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IUnitService
{
    Task<PagedResult<UnitResponse>> ListAsync(UnitListRequest request, CancellationToken cancellationToken);
}

public interface IProductService
{
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ProductListItem>> ListAsync(ProductListRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> ChangeStatusAsync(Guid id, ChangeProductStatusRequest request, CancellationToken cancellationToken);

    // Semantic delete: DISCONTINUED, and the product leaves the store's sale list. The row and SKU are kept.
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse> AddPackagingAsync(Guid productId, PackagingRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdatePackagingAsync(
        Guid productId,
        Guid packagingId,
        UpdatePackagingRequest request,
        CancellationToken cancellationToken);

    // Soft delete; refused when the packaging is already used by prices, receipts, carts, orders or recommendations.
    Task<ProductResponse> DeletePackagingAsync(Guid productId, Guid packagingId, CancellationToken cancellationToken);

    Task<ProductResponse> SetIngredientsAsync(
        Guid productId,
        SetProductIngredientsRequest request,
        CancellationToken cancellationToken);
}

public interface IStoreProductService
{
    Task<StoreProductResponse> CreateAsync(CreateStoreProductRequest request, CancellationToken cancellationToken);

    Task<StoreProductResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<StoreProductResponse>> ListAsync(StoreProductListRequest request, CancellationToken cancellationToken);

    Task<StoreProductResponse> UpdateAsync(Guid id, UpdateStoreProductRequest request, CancellationToken cancellationToken);

    Task SetSellableAsync(Guid id, bool sellable, CancellationToken cancellationToken);

    // Deactivating (and DELETE) takes the product off the store's sale list; the row is kept.
    Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
}

// Public, read-only catalog for farmers and visitors.
public interface ICatalogService
{
    Task<IReadOnlyList<CategoryTreeNode>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<PagedResult<PublicBrand>> GetBrandsAsync(PaginationRequest request, CancellationToken cancellationToken);

    Task<PagedResult<PublicProductListItem>> GetProductsAsync(CatalogProductListRequest request, CancellationToken cancellationToken);

    Task<PublicProductResponse> GetProductAsync(Guid storeProductId, CancellationToken cancellationToken);
}
