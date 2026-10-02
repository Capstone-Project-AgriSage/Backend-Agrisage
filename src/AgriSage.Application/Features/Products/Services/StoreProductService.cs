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

// Products offered by the single active store. A product can be added or marked sellable only when
// CatalogRules.SaleBlocker allows it (ACTIVE product with an ACTIVE base and an ACTIVE sale packaging).
public sealed class StoreProductService(IAgriSageDbContext context, IDatabaseErrorClassifier databaseErrors) : IStoreProductService
{
    private sealed record Row(
        Guid Id,
        Guid ProductId,
        string Sku,
        string Name,
        string? ImageUrl,
        ProductStatus ProductStatus,
        string? StoreSku,
        long? MinStockLevelBase,
        bool IsSellable,
        bool IsActive);

    public async Task<StoreProductResponse> CreateAsync(CreateStoreProductRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var product = await context.Products.AsNoTracking().Include(p => p.Packagings)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        EnsureCanBeSold(product);

        // UNIQUE(store_id, product_id) also counts deleted rows.
        if (await context.StoreProducts.IgnoreQueryFilters()
                .AnyAsync(sp => sp.StoreId == storeId && sp.ProductId == request.ProductId, cancellationToken))
        {
            throw new ConflictException("The product is already in the store's product list.");
        }

        var storeProduct = new StoreProduct(storeId, request.ProductId, Texts.Clean(request.StoreSku), request.MinStockLevelBase);
        context.StoreProducts.Add(storeProduct);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("The product is already in the store's product list.");
        }

        return await GetAsync(storeProduct.Id, cancellationToken);
    }

    public async Task<StoreProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var row = await Project(context.StoreProducts.AsNoTracking().Where(sp => sp.StoreId == storeId && sp.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Store product", id);

        return ToResponse(row);
    }

    public async Task<PagedResult<StoreProductResponse>> ListAsync(StoreProductListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.StoreProducts.AsNoTracking().Where(sp => sp.StoreId == storeId);

        if (request.IsActive is not null)
        {
            query = query.Where(sp => sp.IsActive == request.IsActive);
        }

        if (request.IsSellable is not null)
        {
            query = query.Where(sp => sp.IsSellable == request.IsSellable);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(sp => sp.Product.Name.ToLower().Contains(term)
                || sp.Product.Sku.ToLower().Contains(term)
                || (sp.StoreSku != null && sp.StoreSku.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await Project(query.OrderBy(sp => sp.Product.Name).ThenBy(sp => sp.Id).Skip(request.Skip).Take(request.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<StoreProductResponse>(rows.Select(ToResponse).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<StoreProductResponse> UpdateAsync(Guid id, UpdateStoreProductRequest request, CancellationToken cancellationToken)
    {
        var storeProduct = await FindAsync(id, cancellationToken);

        storeProduct.UpdateSettings(Texts.Clean(request.StoreSku), request.MinStockLevelBase);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task SetSellableAsync(Guid id, bool sellable, CancellationToken cancellationToken)
    {
        var storeProduct = await FindAsync(id, cancellationToken);

        if (sellable)
        {
            EnsureCanBeSold(storeProduct.Product);
            storeProduct.MarkSellable();
        }
        else
        {
            storeProduct.MarkNotSellable();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var storeProduct = await FindAsync(id, cancellationToken);
        if (active)
        {
            storeProduct.Activate();
        }
        else
        {
            storeProduct.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<StoreProduct> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.StoreProducts.Include(sp => sp.Product).ThenInclude(p => p.Packagings)
            .FirstOrDefaultAsync(sp => sp.StoreId == storeId && sp.Id == id, cancellationToken)
            ?? throw new NotFoundException("Store product", id);
    }

    private static void EnsureCanBeSold(Product product)
    {
        var blocker = CatalogRules.SaleBlocker(
            product.Status, product.Packagings.Select(p => (p.IsBaseUnit, p.IsSaleUnit, p.Status)));

        if (blocker is not null)
        {
            throw new BusinessRuleException(blocker);
        }
    }

    private static IQueryable<Row> Project(IQueryable<StoreProduct> query) => query.Select(sp => new Row(
        sp.Id, sp.ProductId, sp.Product.Sku, sp.Product.Name, sp.Product.ImageUrl, sp.Product.Status,
        sp.StoreSku, sp.MinStockLevelBase, sp.IsSellable, sp.IsActive));

    private static StoreProductResponse ToResponse(Row r) => new(
        r.Id, r.ProductId, r.Sku, r.Name, r.ImageUrl, ProductService.StatusText(r.ProductStatus),
        r.StoreSku, r.MinStockLevelBase, r.IsSellable, r.IsActive);
}
