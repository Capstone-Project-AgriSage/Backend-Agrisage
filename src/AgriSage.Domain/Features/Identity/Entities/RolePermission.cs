using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Identity.Entities;

public sealed class RolePermission : AuditableEntity
{
    private RolePermission() { }
    public RolePermission(Guid roleId, Guid permissionId, bool isGranted)
    { RoleId = roleId; PermissionId = permissionId; IsGranted = isGranted; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public bool IsGranted { get; private set; }
    public void SetGrant(bool granted) => IsGranted = granted;
}
