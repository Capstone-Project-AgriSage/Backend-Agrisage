using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Pricing;

// Price lists and their items (task F1.1, FLOW_1 §3). Every write is audited (audit_logs) in the same save.
// The group ↔ price list links are L3's (FLOW_3 §3); the resolution algorithm is PriceResolver.
public sealed class PriceListService(
    IAgriSageDbContext context,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IPriceListService
{
    private const string EntityType = "PRICE_LIST";

    public async Task<PriceListResponse> CreateAsync(PriceListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var code = request.Code.Trim();
        await EnsureCodeIsFreeAsync(storeId, code, cancellationToken);

        var list = new PriceList(
            storeId, code, request.Name.Trim(), request.EffectiveFrom, request.EffectiveTo, request.IsWalkInDefault,
            Texts.Clean(request.Description));
        context.PriceLists.Add(list);
        audit.Record("PRICE_LIST_CREATED", EntityType, list.Id, storeId, newValues: Snapshot(list));
        await SaveAsync(cancellationToken);

        return await GetAsync(list.Id, cancellationToken);
    }

    public async Task<PriceListResponse> UpdateAsync(Guid id, UpdatePriceListRequest request, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);
        var before = Snapshot(list);

        if (request.IsWalkInDefault && !list.IsWalkInDefault && list.Status == PriceListStatus.Active)
        {
            await EnsureNoOtherActiveWalkInDefaultAsync(list, cancellationToken);
        }

        list.Update(request.Name.Trim(), Texts.Clean(request.Description));
        list.ChangeValidity(request.EffectiveFrom, request.EffectiveTo);
        list.SetWalkInDefault(request.IsWalkInDefault);
        audit.Record("PRICE_LIST_UPDATED", EntityType, list.Id, list.StoreId, before, Snapshot(list));
        await SaveAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<PriceListResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var list = await context.PriceLists.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Price list", id);

        return (await ToResponsesAsync([list], cancellationToken))[0];
    }

    public async Task<PagedResult<PriceListResponse>> ListAsync(PriceListListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.PriceLists.AsNoTracking().Where(p => p.StoreId == storeId);

        if (EnumText.TryParse<PriceListStatus>(request.Status, out var status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (request.IsWalkInDefault is { } walkIn)
        {
            query = query.Where(p => p.IsWalkInDefault == walkIn);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Code.ToLower().Contains(term) || p.Name.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var lists = await query.OrderByDescending(p => p.EffectiveFrom).ThenBy(p => p.Code)
            .Skip(request.Skip).Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<PriceListResponse>(
            await ToResponsesAsync(lists, cancellationToken), request.Page, request.PageSize, total);
    }

    public async Task<PriceListResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);
        if (list.IsWalkInDefault)
        {
            await EnsureNoOtherActiveWalkInDefaultAsync(list, cancellationToken);
        }

        list.Activate();
        audit.Record("PRICE_LIST_ACTIVATED", EntityType, list.Id, list.StoreId);
        await SaveAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<PriceListResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);

        list.Deactivate();
        audit.Record("PRICE_LIST_DEACTIVATED", EntityType, list.Id, list.StoreId);
        await SaveAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);

        if (list.Status != PriceListStatus.Draft)
        {
            throw new ConflictException("Only a DRAFT price list can be deleted; deactivate it instead.");
        }

        if (await context.CustomerGroupPriceLists.IgnoreQueryFilters().AnyAsync(l => l.PriceListId == id, cancellationToken))
        {
            throw new ConflictException("The price list was linked to a customer group; deactivate it instead.");
        }

        if (await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.PriceListIdSnapshot == id, cancellationToken))
        {
            throw new ConflictException("Orders were priced with this list; deactivate it instead.");
        }

        context.PriceLists.Remove(list);
        audit.Record("PRICE_LIST_DELETED", EntityType, list.Id, list.StoreId, Snapshot(list));
        await SaveAsync(cancellationToken);
    }

    public async Task<PagedResult<PriceListItemResponse>> ListItemsAsync(
        Guid id, PriceListItemListRequest request, CancellationToken cancellationToken)
    {
        await FindAsync(id, cancellationToken, tracking: false);
        var query = context.PriceListItems.AsNoTracking().Where(i => i.PriceListId == id);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(i => i.StoreProduct.Product.Name.ToLower().Contains(term)
                || i.StoreProduct.Product.Sku.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(i => i.StoreProduct.Product.Name).ThenBy(i => i.ProductPackaging.ConversionToBase).ThenBy(i => i.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(i => new PriceListItemResponse(
                i.Id, i.StoreProductId, i.ProductPackagingId, i.StoreProduct.Product.Sku, i.StoreProduct.Product.Name,
                i.ProductPackaging.PackagingName ?? i.ProductPackaging.Unit.Name, i.SellingPrice))
            .ToListAsync(cancellationToken);

        return new PagedResult<PriceListItemResponse>(items, request.Page, request.PageSize, total);
    }

    public async Task<UpsertPriceListItemsResult> UpsertItemsAsync(
        Guid id, UpsertPriceListItemsRequest request, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);
        var inputs = request.Items;
        var storeProductIds = inputs.Select(i => i.StoreProductId).Distinct().ToList();
        var packagingIds = inputs.Select(i => i.ProductPackagingId).Distinct().ToList();

        var storeProducts = await context.StoreProducts.Include(sp => sp.Product)
            .Where(sp => sp.StoreId == list.StoreId && storeProductIds.Contains(sp.Id))
            .ToDictionaryAsync(sp => sp.Id, cancellationToken);
        var packagings = await context.ProductPackagings
            .Where(p => packagingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var problem = !storeProducts.TryGetValue(input.StoreProductId, out var storeProduct)
                ? "The store product does not exist in this store."
                : !packagings.TryGetValue(input.ProductPackagingId, out var packaging)
                    ? "The packaging does not exist."
                    : packaging.ProductId != storeProduct.ProductId
                        ? "The packaging does not belong to this product."
                        : packaging.Status != PackagingStatus.Active || !packaging.IsSaleUnit
                            ? "Only ACTIVE sale packagings can be priced."
                            : null;
            if (problem is not null)
            {
                errors[$"items[{index}]"] = [problem];
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException($"{errors.Count} line(s) cannot be priced; nothing was saved.", errors);
        }

        var existing = await context.PriceListItems.IgnoreQueryFilters()
            .Where(i => i.PriceListId == id && storeProductIds.Contains(i.StoreProductId))
            .ToDictionaryAsync(i => (i.StoreProductId, i.ProductPackagingId), cancellationToken);

        var created = new List<object>();
        var updated = new List<object>();
        foreach (var input in inputs)
        {
            if (existing.TryGetValue((input.StoreProductId, input.ProductPackagingId), out var item))
            {
                if (item.IsDeleted)
                {
                    item.Reinstate(input.SellingPrice);
                    created.Add(new { input.StoreProductId, input.ProductPackagingId, input.SellingPrice });
                }
                else if (item.SellingPrice != input.SellingPrice)
                {
                    updated.Add(new
                    {
                        input.StoreProductId, input.ProductPackagingId, OldPrice = item.SellingPrice, NewPrice = input.SellingPrice
                    });
                    item.ChangeSellingPrice(input.SellingPrice);
                }
            }
            else
            {
                context.PriceListItems.Add(new PriceListItem(
                    id, storeProducts[input.StoreProductId], packagings[input.ProductPackagingId], input.SellingPrice));
                created.Add(new { input.StoreProductId, input.ProductPackagingId, input.SellingPrice });
            }
        }

        if (created.Count + updated.Count > 0)
        {
            audit.Record("PRICE_LIST_ITEMS_CHANGED", EntityType, id, list.StoreId, newValues: new { created, updated });
        }

        await SaveAsync(cancellationToken);

        return new UpsertPriceListItemsResult(created.Count, updated.Count);
    }

    public async Task DeleteItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);
        var item = await context.PriceListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.PriceListId == id, cancellationToken)
            ?? throw new NotFoundException("Price list item", itemId);

        context.PriceListItems.Remove(item);
        audit.Record(
            "PRICE_LIST_ITEM_REMOVED", EntityType, id, list.StoreId,
            new { item.StoreProductId, item.ProductPackagingId, item.SellingPrice });
        await SaveAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<PriceListResponse>> ToResponsesAsync(
        IReadOnlyList<PriceList> lists, CancellationToken cancellationToken)
    {
        var ids = lists.Select(l => l.Id).ToList();
        var now = clock.UtcNow;

        var counts = await context.PriceListItems.AsNoTracking()
            .Where(i => ids.Contains(i.PriceListId))
            .GroupBy(i => i.PriceListId)
            .Select(g => new { PriceListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PriceListId, x => x.Count, cancellationToken);

        // Groups linked now or from a future date (links not ended yet).
        var groups = await context.CustomerGroupPriceLists.AsNoTracking()
            .Where(l => ids.Contains(l.PriceListId) && (l.EffectiveTo == null || l.EffectiveTo > now))
            .Join(context.CustomerGroups, l => l.CustomerGroupId, g => g.Id, (l, g) => new { l.PriceListId, g.Id, g.Code, g.Name })
            .ToListAsync(cancellationToken);

        return lists.Select(l => new PriceListResponse(
            l.Id, l.Code, l.Name, l.Description, l.EffectiveFrom, l.EffectiveTo, l.IsWalkInDefault, EnumText.Format(l.Status),
            counts.GetValueOrDefault(l.Id),
            groups.Where(g => g.PriceListId == l.Id).OrderBy(g => g.Code)
                .Select(g => new PriceListGroupReference(g.Id, g.Code, g.Name)).ToList(),
            l.CreatedAt)).ToList();
    }

    private async Task<PriceList> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = tracking ? context.PriceLists.AsQueryable() : context.PriceLists.AsNoTracking();

        return await query.FirstOrDefaultAsync(p => p.Id == id && p.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Price list", id);
    }

    // No unique index on (store_id, code): checked here, case-insensitively, among the store's lists (decision Q3).
    private async Task EnsureCodeIsFreeAsync(Guid storeId, string code, CancellationToken cancellationToken)
    {
        var lower = code.ToLower();
        if (await context.PriceLists.AnyAsync(p => p.StoreId == storeId && p.Code.ToLower() == lower, cancellationToken))
        {
            throw new ConflictException($"A price list with code '{code}' already exists.");
        }
    }

    private async Task EnsureNoOtherActiveWalkInDefaultAsync(PriceList list, CancellationToken cancellationToken)
    {
        if (await context.PriceLists.AnyAsync(
                p => p.StoreId == list.StoreId && p.Id != list.Id && p.IsWalkInDefault && p.Status == PriceListStatus.Active,
                cancellationToken))
        {
            throw new BusinessRuleException("Another walk-in default price list is already ACTIVE; deactivate it first.");
        }
    }

    // The partial unique index ux_price_lists_walk_in_default and the item pair index are the backstop for races.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("A conflicting price list change was saved at the same time; reload and try again.");
        }
    }

    private static object Snapshot(PriceList list) => new
    {
        list.Code,
        list.Name,
        list.Description,
        list.EffectiveFrom,
        list.EffectiveTo,
        list.IsWalkInDefault,
        Status = EnumText.Format(list.Status)
    };
}
