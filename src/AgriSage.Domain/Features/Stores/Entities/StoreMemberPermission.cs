using AgriSage.Domain.Common;

namespace AgriSage.Domain.Features.Stores.Entities;

public sealed class StoreMemberPermission : AuditableEntity
{
    private StoreMemberPermission() { }
    public StoreMemberPermission(Guid storeMemberId, Guid permissionId, bool isGranted)
    { StoreMemberId = storeMemberId; PermissionId = permissionId; IsGranted = isGranted; }
    public Guid StoreMemberId { get; private set; }
    public Guid PermissionId { get; private set; }
    public bool IsGranted { get; private set; }
    public void SetGrant(bool granted) => IsGranted = granted;
}
