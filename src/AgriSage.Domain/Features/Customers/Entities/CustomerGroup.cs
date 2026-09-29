using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Customers.Entities;

// "Only one active default group per Store" is enforced by Application + partial unique index.
public sealed class CustomerGroup : SoftDeletableEntity
{
    private CustomerGroup()
    {
    }

    public CustomerGroup(Guid storeId, string code, string name, string? description = null, int priority = 0)
    {
        StoreId = storeId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Update(name, description, priority);
        IsActive = true;
    }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public int Priority { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string name, string? description, int priority)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        Priority = priority;
    }

    public void SetAsDefault() => IsDefault = true;

    public void UnsetDefault() => IsDefault = false;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
