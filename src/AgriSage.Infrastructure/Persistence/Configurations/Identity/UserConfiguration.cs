using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Identity;

// Table 2: users. UNIQUE LOWER(email) is raw SQL (PostgreSqlRawIndexes.UsersEmailLower).
internal sealed class UserConfiguration : EntityConfiguration<User>
{
    protected override string TableName => "users";

    protected override void ConfigureEntity(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).HasMaxLength(255);
        builder.Property(user => user.PhoneNumber).HasMaxLength(20);
        builder.Property(user => user.PasswordHash).HasMaxLength(500);
        builder.Property(user => user.FullName).HasMaxLength(150);
        builder.Property(user => user.AvatarUrl).HasMaxLength(1000);
        builder.Property(user => user.Status).HasMaxLength(30);
        builder.Property(user => user.EmailVerified).HasDbDefault(false);
        builder.Property(user => user.PhoneVerified).HasDbDefault(false);
        builder.Property(user => user.SecurityVersion).HasDbDefault(0L).IsConcurrencyToken();
        builder.HasCheck("security_version", "security_version >= 0");

        builder.HasOne(user => user.Role).WithMany().HasForeignKey(user => user.RoleId);

        builder.HasIndex(user => user.Status);
        builder.HasIndex(user => user.PhoneNumber)
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND phone_number IS NOT NULL");

        // At least one of email / phone number; blank values do not count (database design §35.14).
        builder.HasCheck(
            "contact",
            "NULLIF(BTRIM(email), '') IS NOT NULL OR NULLIF(BTRIM(phone_number), '') IS NOT NULL");
    }
}
