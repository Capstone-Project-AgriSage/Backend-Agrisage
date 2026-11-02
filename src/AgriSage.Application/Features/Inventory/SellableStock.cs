using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// What can be sold of a store product today, in base units: ACTIVE lots that have not expired, quantity on hand minus
// quantity reserved. One formula for the credit-order check and for the advisory stock status (api-flows/ADVISORY.md V7).
// Read only: it never reserves or changes stock.
public static class SellableStock
{
    public static async Task<IReadOnlyDictionary<Guid, long>> AvailableBaseAsync(
        IAgriSageDbContext context,
        IReadOnlyCollection<Guid> storeProductIds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (storeProductIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var rows = await context.InventoryLots.AsNoTracking()
            .Where(l => storeProductIds.Contains(l.StoreProductId)
                && l.Status == InventoryLotStatus.Active
                && (l.ExpiryDate == null || l.ExpiryDate >= today))
            .GroupBy(l => l.StoreProductId)
            .Select(g => new { StoreProductId = g.Key, Available = g.Sum(l => l.Balance.QuantityOnHand - l.Balance.QuantityReserved) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.StoreProductId, r => Math.Max(0L, r.Available));
    }
}
