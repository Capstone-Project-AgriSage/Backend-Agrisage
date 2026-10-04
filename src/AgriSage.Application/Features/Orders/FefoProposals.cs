using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// What FEFO would take from the sellable stock for some demands, read only (nothing is reserved): used by the suggestions
// route of a pending order and by the preview of the quick counter sale (task F1.7). Lots are ACTIVE, not expired, with
// available stock; `AvailableBaseQuantity` is the lot's available stock before these demands, and only lots with a
// suggested quantity are listed. The result follows the order of `demands` (RemainingBaseQuantity = the demand).
public static class FefoProposals
{
    public static async Task<IReadOnlyList<FefoItemSuggestion>> ProposeAsync(
        IAgriSageDbContext context, DateOnly today, IReadOnlyList<FefoDemand> demands, CancellationToken cancellationToken)
    {
        var productIds = demands.Select(d => d.StoreProductId).Distinct().ToList();
        var lots = await context.InventoryLots.AsNoTracking().Include(l => l.Balance)
            .Where(l => productIds.Contains(l.StoreProductId)
                && l.Status == InventoryLotStatus.Active
                && (l.ExpiryDate == null || l.ExpiryDate >= today)
                && l.Balance.QuantityOnHand - l.Balance.QuantityReserved > 0)
            .ToListAsync(cancellationToken);
        var byId = lots.ToDictionary(l => l.Id);

        var allocations = FefoAllocator.Allocate(
            demands,
            lots.Select(l => new FefoLot(l.Id, l.StoreProductId, l.ExpiryDate, l.CreatedAt, l.Balance.AvailableQuantity)).ToList());

        return demands.Select(demand =>
        {
            var allocation = allocations.Single(a => a.OrderItemId == demand.OrderItemId);

            return new FefoItemSuggestion(
                demand.OrderItemId,
                demand.BaseQuantity,
                demand.BaseQuantity,
                allocation.Picks.Select(p => new FefoLotSuggestion(
                    p.LotId, byId[p.LotId].LotNumber, byId[p.LotId].ExpiryDate, byId[p.LotId].Balance.AvailableQuantity, p.BaseQuantity)).ToList(),
                allocation.ShortageBaseQuantity);
        }).ToList();
    }
}
