using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RolePermissionConfiguration : EntityConfiguration<RolePermission>
{
    protected override string TableName => "role_permissions";
    protected override void ConfigureEntity(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasOne<Role>().WithMany().HasForeignKey(p => p.RoleId);
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionId);
        builder.HasIndex(p => new { p.RoleId, p.PermissionId }).IsUnique();
    }
}
