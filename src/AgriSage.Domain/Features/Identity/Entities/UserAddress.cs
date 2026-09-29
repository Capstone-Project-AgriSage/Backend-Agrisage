using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class UserAddress : SoftDeletableEntity
{
    private UserAddress()
    {
    }

    public UserAddress(
        Guid userId,
        string recipientName,
        string recipientPhone,
        string addressLine,
        string province,
        AddressType addressType,
        string? ward = null,
        string? district = null,
        decimal? latitude = null,
        decimal? longitude = null)
    {
        UserId = userId;
        Update(recipientName, recipientPhone, addressLine, province, addressType, ward, district, latitude, longitude);
    }

    public Guid UserId { get; private set; }

    public string RecipientName { get; private set; } = null!;

    public string RecipientPhone { get; private set; } = null!;

    public string AddressLine { get; private set; } = null!;

    public string? Ward { get; private set; }

    public string? District { get; private set; }

    public string Province { get; private set; } = null!;

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public AddressType AddressType { get; private set; }

    // "Maximum one active default address per user" is enforced by Application + partial unique index.
    public bool IsDefault { get; private set; }

    public void Update(
        string recipientName,
        string recipientPhone,
        string addressLine,
        string province,
        AddressType addressType,
        string? ward,
        string? district,
        decimal? latitude,
        decimal? longitude)
    {
        RecipientName = Guard.NotNullOrWhiteSpace(recipientName);
        RecipientPhone = Guard.NotNullOrWhiteSpace(recipientPhone);
        AddressLine = Guard.NotNullOrWhiteSpace(addressLine);
        Province = Guard.NotNullOrWhiteSpace(province);
        AddressType = addressType;
        Ward = ward;
        District = district;
        Latitude = latitude;
        Longitude = longitude;
    }

    public void MarkAsDefault() => IsDefault = true;

    public void UnmarkDefault() => IsDefault = false;
}
