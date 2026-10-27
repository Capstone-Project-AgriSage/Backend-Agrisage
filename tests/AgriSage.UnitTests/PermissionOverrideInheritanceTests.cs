using AgriSage.Domain.Features.Stores.Entities;

namespace AgriSage.UnitTests;

public sealed class PermissionOverrideInheritanceTests
{
    [Fact]
    public void Reset_retains_the_override_identity_and_allows_future_role_inheritance()
    {
        var member = Guid.NewGuid(); var permission = Guid.NewGuid();
        var row = new StoreMemberPermission(member, permission, true);
        var id = row.Id;
        row.SetGrant(false);
        Assert.False(row.IsGranted);
        row.SetGrant(null);
        Assert.Null(row.IsGranted);
        Assert.Equal((id, member, permission), (row.Id, row.StoreMemberId, row.PermissionId));
        row.SetGrant(true);
        Assert.True(row.IsGranted);
    }
}
