using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

internal sealed class AuthSessionConfiguration : EntityConfiguration<AuthSession>
{
    protected override string TableName => "auth_sessions";
    protected override void ConfigureEntity(EntityTypeBuilder<AuthSession> builder)
    {
        builder.HasReference<AuthSession, User>(s => s.UserId);
        builder.Property(s => s.RevocationReason).HasMaxLength(100);
        builder.HasCheck("security_version", "security_version >= 0");
        builder.HasIndex(s => new { s.UserId, s.ExpiresAt });
    }
}
