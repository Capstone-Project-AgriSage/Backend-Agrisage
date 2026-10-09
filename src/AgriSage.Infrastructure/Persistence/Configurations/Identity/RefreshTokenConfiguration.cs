using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

internal sealed class RefreshTokenConfiguration : EntityConfiguration<RefreshToken>
{
    protected override string TableName => "refresh_tokens";
    protected override void ConfigureEntity(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasReference<RefreshToken, AuthSession>(t => t.SessionId);
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.SessionId, t.ExpiresAt });
    }
}
