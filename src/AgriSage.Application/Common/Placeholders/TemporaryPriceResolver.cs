using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Pricing;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task B3 registers the real IPriceResolver (then delete this file and its registration).
// No invented prices: ordering is refused (422) until price lists exist; A1/A2 tests use their own fake.
public sealed class TemporaryPriceResolver : IPriceResolver
{
    public Task<PriceContext> GetContextAsync(Guid? farmerProfileId, DateTimeOffset at, CancellationToken cancellationToken) =>
        throw NotAvailable();

    public Task<IReadOnlyDictionary<PriceLine, decimal>> GetPricesAsync(
        Guid priceListId, IReadOnlyCollection<PriceLine> lines, CancellationToken cancellationToken) =>
        throw NotAvailable();

    private static BusinessRuleException NotAvailable() =>
        new("Pricing is not available yet: price lists arrive with task B3.");
}
