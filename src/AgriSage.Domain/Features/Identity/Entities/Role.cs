using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Domain.Features.Identity.Entities;

// Primary role of a User (one per User). AI review is a StoreMember permission, not a role.
public sealed class Role : SoftDeletableEntity, IHasConcurrencyVersion
{
    private Role()
    {
    }

    public Role(RoleCode code, string name, string? description = null)
    {
        Code = code;
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        IsActive = true;
    }

    public RoleCode Code { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public void MarkPermissionsChanged() => Version++;

    public void Update(string name, string? description)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
