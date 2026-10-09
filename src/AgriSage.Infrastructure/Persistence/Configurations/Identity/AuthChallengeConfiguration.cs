using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

internal sealed class AuthChallengeConfiguration : EntityConfiguration<AuthChallenge>
{
    protected override string TableName => "auth_challenges";
    protected override void ConfigureEntity(EntityTypeBuilder<AuthChallenge> builder)
    {
        builder.HasReference<AuthChallenge, User>(c => c.UserId);
        builder.Property(c => c.Purpose).HasMaxLength(30);
        builder.Property(c => c.Channel).HasMaxLength(20);
        builder.Property(c => c.Destination).HasMaxLength(255);
        builder.Property(c => c.TokenHash).HasMaxLength(64);
        builder.Property(c => c.FailedAttempts).HasDbDefault(0);
        builder.HasCheck("security_version", "security_version >= 0");
        builder.HasCheck("failed_attempts", "failed_attempts BETWEEN 0 AND 5");
        builder.HasCheck("purpose_channel", "purpose = 'PASSWORD_RESET' OR (purpose = 'EMAIL_VERIFICATION' AND channel = 'EMAIL') OR (purpose = 'PHONE_VERIFICATION' AND channel = 'SMS')");
        builder.HasIndex(c => new { c.UserId, c.Purpose, c.CreatedAt }).IsDescending(false, false, true);
    }
}
