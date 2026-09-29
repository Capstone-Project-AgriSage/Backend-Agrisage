using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Orders;

// Destination snapshot taken by a DELIVERY Order and again by each Delivery,
// so later edits to user_addresses never rewrite history.
public sealed record DeliveryAddress(
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string Province,
    string? Ward = null,
    string? District = null,
    decimal? Latitude = null,
    decimal? Longitude = null)
{
    internal void Validate()
    {
        Guard.NotNullOrWhiteSpace(RecipientName);
        Guard.NotNullOrWhiteSpace(RecipientPhone);
        Guard.NotNullOrWhiteSpace(AddressLine);
        Guard.NotNullOrWhiteSpace(Province);
    }
}
