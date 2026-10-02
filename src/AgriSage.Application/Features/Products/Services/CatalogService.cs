using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Products.Services;

// Public catalog: only the store's ACTIVE and sellable products of ACTIVE products; nothing internal is exposed.
public sealed class CatalogService(IAgriSageDbContext context) : ICatalogService
{
    public async Task<IReadOnlyList<CategoryTreeNode>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var all = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);

        return CatalogRules.BuildTree(all.Select(CategoryService.ToResponse).ToList(), activeOnly: true);
    }

    public async Task<PagedResult<PublicBrand>> GetBrandsAsync(PaginationRequest request, CancellationToken cancellationToken)
    {
        var query = context.Brands.AsNoTracking().Where(b => b.IsActive);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(b => b.Name).ThenBy(b => b.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(b => new PublicBrand(b.Id, b.Name, b.LogoUrl))
            .ToListAsync(cancellationToken);

        return new PagedResult<PublicBrand>(items, request.Page, request.PageSize, total);
    }

    public async Task<PagedResult<PublicProductListItem>> GetProductsAsync(
        CatalogProductListRequest request,
        CancellationToken cancellationToken)
    {
        var query = Sellable(await ActiveStore.GetIdAsync(context, cancellationToken));

        if (request.CategoryId is not null)
        {
            var categories = await context.Categories.AsNoTracking().Where(c => c.IsActive)
                .Select(c => new { c.Id, c.ParentId }).ToListAsync(cancellationToken);
            var ids = CatalogRules.SubtreeIds(request.CategoryId.Value, categories.Select(c => (c.Id, c.ParentId))).ToArray();
            query = query.Where(sp => ids.Contains(sp.Product.CategoryId));
        }

        if (request.BrandId is not null)
        {
            query = query.Where(sp => sp.Product.BrandId == request.BrandId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(sp => sp.Product.Name.ToLower().Contains(term) || sp.Product.Sku.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(sp => sp.Product.Name).ThenBy(sp => sp.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(sp => new PublicProductListItem(
                sp.Id, sp.Product.Sku, sp.Product.Name, sp.Product.ImageUrl, sp.Product.CategoryId, sp.Product.Category.Name,
                sp.Product.Brand != null ? sp.Product.Brand.Name : null))
            .ToListAsync(cancellationToken);

        return new PagedResult<PublicProductListItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<PublicProductResponse> GetProductAsync(Guid storeProductId, CancellationToken cancellationToken)
    {
        var storeProduct = await Sellable(await ActiveStore.GetIdAsync(context, cancellationToken))
            .Include(sp => sp.Product).ThenInclude(p => p.Category)
            .Include(sp => sp.Product).ThenInclude(p => p.Brand)
            .FirstOrDefaultAsync(sp => sp.Id == storeProductId, cancellationToken)
            ?? throw new NotFoundException("Product", storeProductId);

        var product = storeProduct.Product;

        // Sale packagings plus the base packaging (every conversion is expressed in the base unit).
        var packagings = await context.ProductPackagings.AsNoTracking()
            .Where(p => p.ProductId == product.Id && p.Status == PackagingStatus.Active && (p.IsSaleUnit || p.IsBaseUnit))
            .OrderByDescending(p => p.IsBaseUnit).ThenBy(p => p.ConversionToBase).ThenBy(p => p.Id)
            .Select(p => new PublicPackaging(
                p.Id, p.Unit.Name, p.Unit.Symbol, p.PackagingName, p.ConversionToBase, p.IsBaseUnit, p.Barcode))
            .ToListAsync(cancellationToken);

        var ingredients = await context.ProductActiveIngredients.AsNoTracking()
            .Where(l => l.ProductId == product.Id)
            .OrderBy(l => l.ActiveIngredient.Name)
            .Select(l => new PublicIngredient(l.ActiveIngredient.Name, l.Concentration))
            .ToListAsync(cancellationToken);

        return new PublicProductResponse(
            storeProduct.Id,
            product.Sku,
            product.Name,
            product.Description,
            product.UsageInstructions,
            product.ImageUrl,
            product.CategoryId,
            product.Category.Name,
            product.BrandId,
            product.Brand?.Name,
            packagings,
            ingredients);
    }

    private IQueryable<StoreProduct> Sellable(Guid storeId) => context.StoreProducts.AsNoTracking()
        .Where(sp => sp.StoreId == storeId && sp.IsActive && sp.IsSellable && sp.Product.Status == ProductStatus.Active);
}
