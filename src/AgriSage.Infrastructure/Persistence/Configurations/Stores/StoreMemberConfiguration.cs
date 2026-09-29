using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Stores;

// Table 6: store_members. UNIQUE(store_id, user_id): a returning member reuses the same row.
internal sealed class StoreMemberConfiguration : EntityConfiguration<StoreMember>
{
    protected override string TableName => "store_members";

    protected override void ConfigureEntity(EntityTypeBuilder<StoreMember> builder)
    {
        builder.Property(member => member.EmployeeCode).HasMaxLength(50);
        builder.Property(member => member.Status).HasMaxLength(20);
        builder.Property(member => member.CanReviewAi).HasDbDefault(false);

        builder.HasReference<StoreMember, Store>(member => member.StoreId);
        builder.HasOne(member => member.User).WithMany().HasForeignKey(member => member.UserId);

        builder.HasIndex(member => new { member.StoreId, member.UserId }).IsUnique();
        builder.HasIndex(member => new { member.UserId, member.Status });
        builder.HasIndex(member => new { member.StoreId, member.Status });
    }
}
