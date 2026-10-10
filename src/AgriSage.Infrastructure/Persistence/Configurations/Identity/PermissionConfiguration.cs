using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

internal sealed class PermissionConfiguration : EntityConfiguration<Permission>
{
    protected override string TableName => "permissions";
    protected override void ConfigureEntity(EntityTypeBuilder<Permission> builder)
    {
        builder.Property(p => p.Code).HasMaxLength(120);
        builder.Property(p => p.Module).HasMaxLength(50);
        builder.Property(p => p.Name).HasMaxLength(150);
        builder.Property(p => p.IsActive).HasDbDefault(true);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasIndex(p => new { p.Module, p.IsActive });
        builder.HasData(AgriSage.Application.Features.Permissions.PermissionCatalog.Entries.Select(p =>
            new { p.Id, p.Code, p.Module, p.Name, IsDelegable = p.Delegable, IsActive = true }));
    }
}
