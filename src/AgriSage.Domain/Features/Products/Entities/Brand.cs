using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Products.Entities;

// Manufacturer/brand of a Product; not the same concept as Supplier.
public sealed class Brand : SoftDeletableEntity
{
    private Brand()
    {
    }

    public Brand(string name, string? code = null, string? description = null, string? logoUrl = null)
    {
        Update(name, code, description, logoUrl);
        IsActive = true;
    }

    public string? Code { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? LogoUrl { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? code, string? description, string? logoUrl)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Code = code;
        Description = description;
        LogoUrl = logoUrl;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
