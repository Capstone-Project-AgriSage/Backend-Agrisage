using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Stores;

internal sealed class StoreMemberPermissionConfiguration : EntityConfiguration<StoreMemberPermission>
{
    protected override string TableName => "store_member_permissions";
    protected override void ConfigureEntity(EntityTypeBuilder<StoreMemberPermission> builder)
    {
        builder.HasOne<StoreMember>().WithMany().HasForeignKey(p => p.StoreMemberId);
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionId);
        builder.HasIndex(p => new { p.StoreMemberId, p.PermissionId }).IsUnique();
    }
}
