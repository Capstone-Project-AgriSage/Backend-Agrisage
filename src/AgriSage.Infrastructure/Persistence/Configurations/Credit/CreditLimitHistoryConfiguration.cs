using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Credit;

// Table 46: credit_limit_histories.
internal sealed class CreditLimitHistoryConfiguration : EntityConfiguration<CreditLimitHistory>
{
    protected override string TableName => "credit_limit_histories";

    protected override void ConfigureEntity(EntityTypeBuilder<CreditLimitHistory> builder)
    {
        builder.Property(history => history.OldCreditLimit).IsMoney();
        builder.Property(history => history.NewCreditLimit).IsMoney();
        builder.Property(history => history.Reason).HasMaxLength(1000);

        builder.HasReference<CreditLimitHistory, CreditTier>(history => history.OldCreditTierId);
        builder.HasReference<CreditLimitHistory, CreditTier>(history => history.NewCreditTierId);
        builder.HasUserReference(history => history.ChangedBy);

        builder.HasCheck("old_credit_limit", "old_credit_limit >= 0");
        builder.HasCheck("new_credit_limit", "new_credit_limit >= 0");
    }
}
