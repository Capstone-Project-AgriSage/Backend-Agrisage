namespace AgriSage.Application.Features.Pricing;

// Cross-flow interface (docs/reference/api-flows/README.md §4.1). Owner: task F1.1. Used by orders and carts
// (F1.2, F1.7, F2.2, F2.3).
// Runs inside the caller's transaction and never saves.
public interface IPriceResolver
{
    // Price list that applies to the customer at the given moment: the Farmer's customer-group list, or the walk-in
    // default list when farmerProfileId is null or the group has none (decision B-D3). No list → BusinessRuleException.
    Task<PriceContext> GetContextAsync(Guid? farmerProfileId, DateTimeOffset at, CancellationToken cancellationToken);

    // Selling price per (store product, packaging) in that list; a missing pair = no price (the line cannot be ordered).
    Task<IReadOnlyDictionary<PriceLine, decimal>> GetPricesAsync(
        Guid priceListId, IReadOnlyCollection<PriceLine> lines, CancellationToken cancellationToken);
}

public sealed record PriceContext(Guid? CustomerGroupId, Guid PriceListId);

public readonly record struct PriceLine(Guid StoreProductId, Guid ProductPackagingId);
