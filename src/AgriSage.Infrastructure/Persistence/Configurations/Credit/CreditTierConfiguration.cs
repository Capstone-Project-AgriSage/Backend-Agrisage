using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Credit;

// Table 44: credit_tiers.
internal sealed class CreditTierConfiguration : EntityConfiguration<CreditTier>
{
    protected override string TableName => "credit_tiers";

    protected override void ConfigureEntity(EntityTypeBuilder<CreditTier> builder)
    {
        builder.Property(tier => tier.Code).HasMaxLength(30);
        builder.Property(tier => tier.Name).HasMaxLength(100);
        builder.Property(tier => tier.Description).HasMaxLength(500);
        builder.Property(tier => tier.DefaultCreditLimit).IsMoney().HasDbDefault(0m);
        builder.Property(tier => tier.DefaultPaymentTermDays).HasDbDefault(0);
        builder.Property(tier => tier.IsActive).HasDbDefault(true);

        builder.HasReference<CreditTier, Store>(tier => tier.StoreId);

        builder.HasIndex(tier => new { tier.StoreId, tier.Code }).IsUnique();

        builder.HasCheck("default_credit_limit", "default_credit_limit >= 0");
        builder.HasCheck("default_payment_term_days", "default_payment_term_days >= 0");
    }
}
