using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

// Table 1: roles.
internal sealed class RoleConfiguration : EntityConfiguration<Role>
{
    protected override string TableName => "roles";

    protected override void ConfigureEntity(EntityTypeBuilder<Role> builder)
    {
        builder.Property(role => role.Code).HasMaxLength(30);
        builder.Property(role => role.Name).HasMaxLength(100);
        builder.Property(role => role.Description).HasMaxLength(500);
        builder.Property(role => role.IsActive).HasDbDefault(true);

        // UNIQUE(code) for active records.
        builder.HasIndex(role => role.Code).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
