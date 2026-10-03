using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Pricing.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Pricing;

// Real IPriceResolver (task F1.1, FLOW_1 §3 "Price resolution"). Reads only; never saves.
// 1. Farmer → current customer-group assignment, or the store's active default group (decision B-D2).
// 2. Group → its current price-list link whose list is ACTIVE and valid at `at`.
// 3. No such list (or a walk-in customer) → the ACTIVE walk-in default list valid at `at` (decision B-D3).
// 4. No list at all → BusinessRuleException (422).
// 5. Prices: price_list_items.selling_price per (store product, packaging); a missing pair = no price.
public sealed class PriceResolver(IAgriSageDbContext context) : IPriceResolver
{
    public async Task<PriceContext> GetContextAsync(Guid? farmerProfileId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        Guid? groupId = null;

        if (farmerProfileId is { } farmerId)
        {
            groupId = await context.CustomerGroupAssignments.AsNoTracking()
                .Where(a => a.FarmerProfileId == farmerId && a.EffectiveTo == null)
                .Join(context.CustomerGroups.Where(g => g.StoreId == storeId), a => a.CustomerGroupId, g => g.Id, (a, g) => (Guid?)g.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await context.CustomerGroups.AsNoTracking()
                    .Where(g => g.StoreId == storeId && g.IsDefault && g.IsActive)
                    .Select(g => (Guid?)g.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (groupId is { } group)
            {
                var groupList = await context.CustomerGroupPriceLists.AsNoTracking()
                    .Where(link => link.CustomerGroupId == group && link.EffectiveFrom <= at
                        && (link.EffectiveTo == null || link.EffectiveTo > at))
                    .Join(
                        context.PriceLists.Where(p => p.StoreId == storeId && p.Status == PriceListStatus.Active
                            && p.EffectiveFrom <= at && (p.EffectiveTo == null || p.EffectiveTo > at)),
                        link => link.PriceListId, list => list.Id, (link, list) => new { link.EffectiveFrom, list.Id })
                    .OrderByDescending(x => x.EffectiveFrom)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (groupList is { } listId)
                {
                    return new PriceContext(group, listId);
                }
            }
        }

        var walkIn = await WalkInDefaultIdAsync(context, storeId, at, cancellationToken)
            ?? throw new BusinessRuleException(
                "No price list applies: activate a walk-in default price list (or link one to the customer's group).");

        return new PriceContext(groupId, walkIn);
    }

    public async Task<IReadOnlyDictionary<PriceLine, decimal>> GetPricesAsync(
        Guid priceListId, IReadOnlyCollection<PriceLine> lines, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return new Dictionary<PriceLine, decimal>();
        }

        var wanted = lines.ToHashSet();
        var storeProductIds = wanted.Select(l => l.StoreProductId).Distinct().ToList();
        var packagingIds = wanted.Select(l => l.ProductPackagingId).Distinct().ToList();
        var rows = await context.PriceListItems.AsNoTracking()
            .Where(i => i.PriceListId == priceListId
                && storeProductIds.Contains(i.StoreProductId) && packagingIds.Contains(i.ProductPackagingId))
            .Select(i => new { i.StoreProductId, i.ProductPackagingId, i.SellingPrice })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => (Line: new PriceLine(r.StoreProductId, r.ProductPackagingId), r.SellingPrice))
            .Where(r => wanted.Contains(r.Line))
            .ToDictionary(r => r.Line, r => r.SellingPrice);
    }

    // The ACTIVE walk-in default list valid at `at`, or null. Also used by the public catalog (FLOW_1 §3).
    public static Task<Guid?> WalkInDefaultIdAsync(
        IAgriSageDbContext context, Guid storeId, DateTimeOffset at, CancellationToken cancellationToken) =>
        context.PriceLists.AsNoTracking()
            .Where(p => p.StoreId == storeId && p.IsWalkInDefault && p.Status == PriceListStatus.Active
                && p.EffectiveFrom <= at && (p.EffectiveTo == null || p.EffectiveTo > at))
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
