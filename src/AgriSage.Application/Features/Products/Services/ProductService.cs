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

// Product aggregate: the product with its packagings (created together, one SaveChanges), plus its ingredient links.
public sealed class ProductService(IAgriSageDbContext context, IDatabaseErrorClassifier databaseErrors,
    AuditTrail? audit = null) : IProductService
{
    private sealed record ProductRow(
        Guid Id,
        string Sku,
        string Name,
        Guid CategoryId,
        string CategoryName,
        Guid? BrandId,
        string? BrandName,
        string? ImageUrl,
        ProductStatus Status);

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim();
        var lowerSku = sku.ToLower();
        // The unique index on sku also counts deleted products.
        if (await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Sku.ToLower() == lowerSku, cancellationToken))
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }

        await EnsureCategoryAndBrandExistAsync(request.CategoryId, request.BrandId, cancellationToken);
        await EnsureUnitsAreUsableAsync(request.Packagings.Select(p => p.UnitId), cancellationToken);
        await EnsureBarcodesAreFreeAsync(request.Packagings.Select(p => p.Barcode), null, cancellationToken);

        var product = new Product(
            request.CategoryId,
            sku,
            request.Name.Trim(),
            request.RequiresLotTracking,
            request.RequiresExpiryDate,
            request.BrandId,
            Texts.Clean(request.Description),
            Texts.Clean(request.UsageInstructions),
            Texts.Clean(request.ImageUrl));

        foreach (var packaging in request.Packagings)
        {
            product.AddPackaging(
                packaging.UnitId,
                packaging.ConversionToBase,
                packaging.IsBaseUnit,
                packaging.IsPurchaseUnit,
                packaging.IsSaleUnit,
                PackagingStatus.Active,
                Texts.Clean(packaging.PackagingName),
                Texts.Clean(packaging.Barcode));
        }

        context.Products.Add(product);
        audit?.Record("PRODUCT_CREATED", "PRODUCT", product.Id, null, newValues: Snapshot(product));
        await SaveAsync(cancellationToken);

        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await context.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Packagings).ThenInclude(pk => pk.Unit)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        var ingredients = await context.ProductActiveIngredients.AsNoTracking()
            .Where(l => l.ProductId == id)
            .OrderBy(l => l.ActiveIngredient.Name)
            .Select(l => new ProductIngredientResponse(l.ActiveIngredientId, l.ActiveIngredient.Name, l.Concentration, l.Note))
            .ToListAsync(cancellationToken);

        var store = await context.StoreProducts.AsNoTracking()
            .Where(sp => sp.ProductId == id)
            .Select(sp => new ProductStoreInfo(sp.Id, sp.IsSellable, sp.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

        var packagings = product.Packagings
            .OrderByDescending(p => p.IsBaseUnit).ThenBy(p => p.ConversionToBase).ThenBy(p => p.Id)
            .Select(ToResponse).ToList();

        return new ProductResponse(
            product.Id,
            product.Sku,
            product.Name,
            product.CategoryId,
            product.Category.Name,
            product.BrandId,
            product.Brand?.Name,
            product.Description,
            product.UsageInstructions,
            product.RequiresLotTracking,
            product.RequiresExpiryDate,
            product.ImageUrl,
            StatusText(product.Status),
            packagings,
            ingredients,
            store);
    }

    public async Task<PagedResult<ProductListItem>> ListAsync(ProductListRequest request, CancellationToken cancellationToken)
    {
        var query = context.Products.AsNoTracking().AsQueryable();

        if (request.CategoryId is not null)
        {
            query = query.Where(p => p.CategoryId == request.CategoryId);
        }

        if (request.BrandId is not null)
        {
            query = query.Where(p => p.BrandId == request.BrandId);
        }

        if (Enum.TryParse<ProductStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(p => new ProductRow(
                p.Id, p.Sku, p.Name, p.CategoryId, p.Category.Name, p.BrandId,
                p.Brand != null ? p.Brand.Name : null, p.ImageUrl, p.Status))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new ProductListItem(
            r.Id, r.Sku, r.Name, r.CategoryId, r.CategoryName, r.BrandId, r.BrandName, r.ImageUrl, StatusText(r.Status))).ToList();

        return new PagedResult<ProductListItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);
        await EnsureCategoryAndBrandExistAsync(request.CategoryId, request.BrandId, cancellationToken);

        var before = Snapshot(product);
        product.UpdateDetails(
            request.CategoryId,
            request.BrandId,
            request.Name.Trim(),
            Texts.Clean(request.Description),
            Texts.Clean(request.UsageInstructions),
            Texts.Clean(request.ImageUrl));
        audit?.Record("PRODUCT_UPDATED", "PRODUCT", product.Id, null, before, Snapshot(product));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<ProductResponse> ChangeStatusAsync(Guid id, ChangeProductStatusRequest request, CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        var before = Snapshot(product);
        product.ChangeStatus(Enum.Parse<ProductStatus>(request.Status, ignoreCase: true));
        audit?.Record("PRODUCT_STATUS_CHANGED", "PRODUCT", product.Id, null, before, Snapshot(product));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Product", id);

        var before = Snapshot(product);
        product.ChangeStatus(ProductStatus.Discontinued);
        foreach (var storeProduct in await context.StoreProducts.Where(sp => sp.ProductId == id).ToListAsync(cancellationToken))
        {
            storeProduct.Deactivate();
            audit?.Record("STORE_PRODUCT_DEACTIVATED", "STORE_PRODUCT", storeProduct.Id, storeProduct.StoreId,
                newValues: new { product.Sku, product.Name, storeProduct.IsActive, storeProduct.IsSellable });
        }

        audit?.Record("PRODUCT_DISCONTINUED", "PRODUCT", product.Id, null, before, Snapshot(product));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductResponse> AddPackagingAsync(Guid productId, PackagingRequest request, CancellationToken cancellationToken)
    {
        var product = await LoadWithPackagingsAsync(productId, cancellationToken);
        await EnsureUnitsAreUsableAsync([request.UnitId], cancellationToken);
        await EnsureBarcodesAreFreeAsync([request.Barcode], null, cancellationToken);

        var before = PackagingSnapshot(product);
        product.AddPackaging(
            request.UnitId,
            request.ConversionToBase,
            request.IsBaseUnit,
            request.IsPurchaseUnit,
            request.IsSaleUnit,
            PackagingStatus.Active,
            Texts.Clean(request.PackagingName),
            Texts.Clean(request.Barcode));
        audit?.Record("PRODUCT_PACKAGING_CREATED", "PRODUCT", product.Id, null, before, PackagingSnapshot(product));
        await SaveAsync(cancellationToken);

        return await GetAsync(productId, cancellationToken);
    }

    public async Task<ProductResponse> UpdatePackagingAsync(
        Guid productId,
        Guid packagingId,
        UpdatePackagingRequest request,
        CancellationToken cancellationToken)
    {
        var product = await LoadWithPackagingsAsync(productId, cancellationToken);
        var packaging = product.Packagings.FirstOrDefault(p => p.Id == packagingId)
            ?? throw new NotFoundException("Packaging", packagingId);
        await EnsureBarcodesAreFreeAsync([request.Barcode], packagingId, cancellationToken);

        // Every conversion points at the base unit, so the base packaging stays active while others are.
        if (request.Status == PackagingStatus.Inactive
            && packaging.IsBaseUnit
            && product.Packagings.Any(p => p.Id != packagingId && p.Status == PackagingStatus.Active))
        {
            throw new BusinessRuleException("The base packaging cannot be deactivated while other packagings are ACTIVE.");
        }

        var before = PackagingSnapshot(product);
        packaging.UpdateDetails(
            Texts.Clean(request.PackagingName), Texts.Clean(request.Barcode), request.IsPurchaseUnit, request.IsSaleUnit);
        packaging.ChangeStatus(request.Status);
        audit?.Record("PRODUCT_PACKAGING_UPDATED", "PRODUCT", product.Id, null, before, PackagingSnapshot(product));
        await SaveAsync(cancellationToken);

        return await GetAsync(productId, cancellationToken);
    }

    public async Task<ProductResponse> DeletePackagingAsync(Guid productId, Guid packagingId, CancellationToken cancellationToken)
    {
        var product = await LoadWithPackagingsAsync(productId, cancellationToken);
        var packaging = product.Packagings.FirstOrDefault(p => p.Id == packagingId)
            ?? throw new NotFoundException("Packaging", packagingId);

        if (packaging.IsBaseUnit && product.Packagings.Any(p => p.Id != packagingId))
        {
            throw new BusinessRuleException("The base packaging cannot be deleted while the product has other packagings.");
        }

        if (await IsPackagingUsedAsync(packagingId, cancellationToken))
        {
            throw new ConflictException("The packaging is already used by prices, receipts, carts or orders; set it to INACTIVE instead.");
        }

        context.ProductPackagings.Remove(packaging);
        audit?.Record("PRODUCT_PACKAGING_REMOVED", "PRODUCT", product.Id, null,
            new { product.Sku, packagingId = packaging.Id, packaging.PackagingName, packaging.Barcode },
            new { product.Sku, packagingId = packaging.Id, status = "REMOVED" });
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(productId, cancellationToken);
    }

    public async Task<ProductResponse> SetIngredientsAsync(
        Guid productId,
        SetProductIngredientsRequest request,
        CancellationToken cancellationToken)
    {
        if (!await context.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw new NotFoundException("Product", productId);
        }

        var wanted = request.Ingredients.Select(i => i.ActiveIngredientId).ToList();
        var usable = await context.ActiveIngredients.CountAsync(i => wanted.Contains(i.Id) && i.IsActive, cancellationToken);
        if (usable != wanted.Count)
        {
            throw new BusinessRuleException("One or more active ingredients do not exist or are inactive.");
        }

        // Deleted links are loaded too: the unique index counts them, so a removed ingredient is revived, not duplicated.
        var links = await context.ProductActiveIngredients.IgnoreQueryFilters()
            .Where(l => l.ProductId == productId).ToListAsync(cancellationToken);
        var before = new { ingredients = links.Where(l => !l.IsDeleted).Select(l => new
            { l.ActiveIngredientId, l.Concentration, l.Note }).ToArray() };

        foreach (var item in request.Ingredients)
        {
            var concentration = Texts.Clean(item.Concentration);
            var note = Texts.Clean(item.Note);
            var link = links.FirstOrDefault(l => l.ActiveIngredientId == item.ActiveIngredientId);

            if (link is null)
            {
                context.ProductActiveIngredients.Add(new ProductActiveIngredient(productId, item.ActiveIngredientId, concentration, note));
            }
            else if (link.IsDeleted)
            {
                link.Reinstate(concentration, note);
            }
            else
            {
                link.Update(concentration, note);
            }
        }

        foreach (var link in links.Where(l => !l.IsDeleted && !wanted.Contains(l.ActiveIngredientId)))
        {
            context.ProductActiveIngredients.Remove(link);
        }

        audit?.Record("PRODUCT_INGREDIENTS_UPDATED", "PRODUCT", productId, null, before,
            new { ingredients = request.Ingredients.Select(i => new
                { i.ActiveIngredientId, concentration = Texts.Clean(i.Concentration), note = Texts.Clean(i.Note) }).ToArray() });
        await SaveAsync(cancellationToken);

        return await GetAsync(productId, cancellationToken);
    }

    private static object Snapshot(Product p) => new { p.Sku, p.Name, p.CategoryId, p.BrandId, p.Description,
        p.UsageInstructions, p.ImageUrl, p.RequiresLotTracking, p.RequiresExpiryDate, status = StatusText(p.Status) };

    private static object PackagingSnapshot(Product p) => new { p.Sku, p.Name, packagings = p.Packagings
        .OrderBy(pk => pk.Id).Select(pk => new { pk.Id, pk.UnitId, pk.ConversionToBase, pk.IsBaseUnit,
            pk.IsPurchaseUnit, pk.IsSaleUnit, pk.PackagingName, pk.Barcode, pk.Status }).ToArray() };

    private async Task<Product> LoadWithPackagingsAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Products.Include(p => p.Packagings).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException("Product", id);

    private async Task EnsureCategoryAndBrandExistAsync(Guid categoryId, Guid? brandId, CancellationToken cancellationToken)
    {
        if (!await context.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new NotFoundException("Category", categoryId);
        }

        if (brandId is not null && !await context.Brands.AnyAsync(b => b.Id == brandId, cancellationToken))
        {
            throw new NotFoundException("Brand", brandId);
        }
    }

    private async Task EnsureUnitsAreUsableAsync(IEnumerable<Guid> unitIds, CancellationToken cancellationToken)
    {
        var ids = unitIds.Distinct().ToList();
        if (await context.Units.CountAsync(u => ids.Contains(u.Id) && u.IsActive, cancellationToken) != ids.Count)
        {
            throw new BusinessRuleException("One or more units do not exist or are inactive.");
        }
    }

    private async Task EnsureBarcodesAreFreeAsync(IEnumerable<string?> barcodes, Guid? exceptPackagingId, CancellationToken cancellationToken)
    {
        var used = barcodes.Select(Texts.Clean).Where(b => b is not null).Select(b => b!).Distinct().ToList();
        if (used.Count == 0)
        {
            return;
        }

        if (await context.ProductPackagings.AsNoTracking().AnyAsync(
                p => p.Barcode != null && used.Contains(p.Barcode) && p.Id != exceptPackagingId, cancellationToken))
        {
            throw new ConflictException("A barcode is already used by another packaging.");
        }
    }

    // Any row that ever pointed at the packaging, deleted or not, keeps it from being deleted.
    private async Task<bool> IsPackagingUsedAsync(Guid packagingId, CancellationToken cancellationToken) =>
        await context.PriceListItems.IgnoreQueryFilters().AnyAsync(x => x.ProductPackagingId == packagingId, cancellationToken)
        || await context.GoodsReceiptItems.IgnoreQueryFilters().AnyAsync(x => x.ProductPackagingId == packagingId, cancellationToken)
        || await context.CartItems.IgnoreQueryFilters().AnyAsync(x => x.ProductPackagingId == packagingId, cancellationToken)
        || await context.OrderItems.IgnoreQueryFilters().AnyAsync(x => x.ProductPackagingId == packagingId, cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("A product, packaging or barcode with the same unique value already exists.");
        }
    }

    internal static string StatusText(ProductStatus status) => status.ToString().ToUpperInvariant();

    private static PackagingResponse ToResponse(ProductPackaging p) => new(
        p.Id, p.UnitId, p.Unit.Code, p.Unit.Name, p.PackagingName, p.ConversionToBase,
        p.IsBaseUnit, p.IsPurchaseUnit, p.IsSaleUnit, p.Barcode, p.Status);
}
