using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Suppliers.Entities;

// Source of received goods (not a Brand). No Purchase Order workflow is managed by AgriSage.
public sealed class Supplier : SoftDeletableEntity
{
    private Supplier()
    {
    }

    public Supplier(
        Guid storeId,
        string name,
        string? code = null,
        string? taxCode = null,
        string? phoneNumber = null,
        string? email = null,
        string? contactPerson = null,
        string? addressLine = null,
        string? ward = null,
        string? district = null,
        string? province = null,
        string? note = null)
    {
        StoreId = storeId;
        UpdateDetails(name, code, taxCode, phoneNumber, email, contactPerson, addressLine, ward, district, province, note);
        IsActive = true;
    }

    public Guid StoreId { get; private set; }

    public string? Code { get; private set; }

    public string Name { get; private set; } = null!;

    public string? TaxCode { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public string? ContactPerson { get; private set; }

    public string? AddressLine { get; private set; }

    public string? Ward { get; private set; }

    public string? District { get; private set; }

    public string? Province { get; private set; }

    public string? Note { get; private set; }

    public bool IsActive { get; private set; }

    public void UpdateDetails(
        string name,
        string? code,
        string? taxCode,
        string? phoneNumber,
        string? email,
        string? contactPerson,
        string? addressLine,
        string? ward,
        string? district,
        string? province,
        string? note)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Code = code;
        TaxCode = taxCode;
        PhoneNumber = phoneNumber;
        Email = email;
        ContactPerson = contactPerson;
        AddressLine = addressLine;
        Ward = ward;
        District = district;
        Province = province;
        Note = note;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
