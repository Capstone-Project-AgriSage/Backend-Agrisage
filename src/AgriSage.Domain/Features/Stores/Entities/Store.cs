using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Stores.Enums;

namespace AgriSage.Domain.Features.Stores.Entities;

// Real entity even though one Store is active; never hard-code its Id.
// "Only one Store may be ACTIVE" is enforced by Application.
public sealed class Store : SoftDeletableEntity
{
    private Store()
    {
    }

    public Store(
        string code,
        string name,
        string addressLine,
        string province,
        string? phoneNumber = null,
        string? email = null,
        string? taxCode = null,
        string? ward = null,
        string? district = null,
        StoreStatus status = StoreStatus.Active)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        UpdateDetails(name, phoneNumber, email, taxCode, addressLine, ward, district, province);
        Status = status;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public string? TaxCode { get; private set; }

    public string AddressLine { get; private set; } = null!;

    public string? Ward { get; private set; }

    public string? District { get; private set; }

    public string Province { get; private set; } = null!;

    public StoreStatus Status { get; private set; }

    public void UpdateDetails(
        string name,
        string? phoneNumber,
        string? email,
        string? taxCode,
        string addressLine,
        string? ward,
        string? district,
        string province)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        PhoneNumber = phoneNumber;
        Email = email;
        TaxCode = taxCode;
        AddressLine = Guard.NotNullOrWhiteSpace(addressLine);
        Ward = ward;
        District = district;
        Province = Guard.NotNullOrWhiteSpace(province);
    }

    public void ChangeStatus(StoreStatus status) => Status = status;
}
