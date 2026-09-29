namespace AgriSage.Domain.Features.Orders;

// Staff override of the suggested Order Item price; actor and reason are always kept.
public sealed record PriceOverride(decimal UnitPrice, Guid OverriddenBy, string Reason);
