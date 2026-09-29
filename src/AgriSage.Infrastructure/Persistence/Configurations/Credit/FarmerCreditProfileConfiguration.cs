using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Credit;

// Table 45: farmer_credit_profiles. Aggregate root for credit_limit_histories.
// `version` is mapped; its concurrency behavior is AGRI-13.
internal sealed class FarmerCreditProfileConfiguration : EntityConfiguration<FarmerCreditProfile>
{
    protected override string TableName => "farmer_credit_profiles";

    protected override void ConfigureEntity(EntityTypeBuilder<FarmerCreditProfile> builder)
    {
        builder.Property(profile => profile.CreditLimit).IsMoney().HasDbDefault(0m);
        builder.Property(profile => profile.Status).HasMaxLength(20);
        builder.Property(profile => profile.Note).HasMaxLength(1000);
        builder.Property(profile => profile.Version).HasDbDefault(0L);

        builder.Ignore(profile => profile.CanUseCredit);

        builder.HasReference<FarmerCreditProfile, Store>(profile => profile.StoreId);
        builder.HasOne(profile => profile.FarmerProfile).WithMany().HasForeignKey(profile => profile.FarmerProfileId);
        builder.HasOne(profile => profile.CreditTier).WithMany().HasForeignKey(profile => profile.CreditTierId);
        builder.HasUserReference(profile => profile.ApprovedBy);
        builder.HasMany(profile => profile.LimitHistories).WithOne().HasForeignKey(history => history.FarmerCreditProfileId);

        builder.HasIndex(profile => new { profile.StoreId, profile.FarmerProfileId }).IsUnique();

        builder.HasCheck("credit_limit", "credit_limit >= 0");
    }
}
