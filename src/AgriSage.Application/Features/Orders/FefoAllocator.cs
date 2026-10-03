namespace AgriSage.Application.Features.Orders;

// A lot that can be sold: eligible (ACTIVE, not expired) with available base quantity.
public sealed record FefoLot(Guid LotId, Guid StoreProductId, DateOnly? ExpiryDate, DateTimeOffset CreatedAt, long AvailableBaseQuantity);

public sealed record FefoDemand(Guid OrderItemId, Guid StoreProductId, long BaseQuantity);

public readonly record struct FefoPick(Guid LotId, long BaseQuantity);

// Picks cover the demand fully when ShortageBaseQuantity is 0; otherwise they are what could be covered.
public sealed record FefoAllocation(Guid OrderItemId, IReadOnlyList<FefoPick> Picks, long ShortageBaseQuantity);

// First Expired, First Out (decision D6): earliest expiry first, lots without expiry last, then the oldest lot (creation
// time, then id). Demands of the same product share the available quantity in the order given. A pure function: the
// caller decides what a shortage means (confirmation reserves nothing when any line is short).
public static class FefoAllocator
{
    public static IReadOnlyList<FefoAllocation> Allocate(IReadOnlyList<FefoDemand> demands, IReadOnlyList<FefoLot> lots)
    {
        var ordered = lots
            .Where(l => l.AvailableBaseQuantity > 0)
            .OrderBy(l => l.ExpiryDate is null ? 1 : 0)
            .ThenBy(l => l.ExpiryDate)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.LotId)
            .ToList();
        var left = ordered.ToDictionary(l => l.LotId, l => l.AvailableBaseQuantity);

        var result = new List<FefoAllocation>(demands.Count);
        foreach (var demand in demands)
        {
            var need = demand.BaseQuantity;
            var picks = new List<FefoPick>();

            foreach (var lot in ordered.Where(l => l.StoreProductId == demand.StoreProductId))
            {
                if (need == 0)
                {
                    break;
                }

                var take = Math.Min(need, left[lot.LotId]);
                if (take <= 0)
                {
                    continue;
                }

                picks.Add(new FefoPick(lot.LotId, take));
                left[lot.LotId] -= take;
                need -= take;
            }

            result.Add(new FefoAllocation(demand.OrderItemId, picks, need));
        }

        return result;
    }
}
