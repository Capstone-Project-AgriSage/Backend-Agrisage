namespace AgriSage.Domain.Features.Inventory;

// Result of one physical Lot balance change; the posting use case copies it into a Stock Movement Item.
// QuantityDelta: positive = stock increase, negative = stock decrease.
public sealed record LotBalanceChange(
    long QuantityDelta,
    decimal UnitCost,
    decimal TotalCost,
    long QuantityOnHandAfter,
    decimal TotalCostValueAfter);
