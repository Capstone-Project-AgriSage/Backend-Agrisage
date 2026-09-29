using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// Table 48: debt_accounts. `version` is mapped; its concurrency behavior is AGRI-13.
internal sealed class DebtAccountConfiguration : EntityConfiguration<DebtAccount>
{
    protected override string TableName => "debt_accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtAccount> builder)
    {
        builder.Property(account => account.CurrentBalance).IsMoney().HasDbDefault(0m);
        builder.Property(account => account.Status).HasMaxLength(20);
        builder.Property(account => account.Version).HasDbDefault(0L);

        builder.HasReference<DebtAccount, Store>(account => account.StoreId);
        builder.HasOne(account => account.FarmerProfile).WithMany().HasForeignKey(account => account.FarmerProfileId);

        builder.HasIndex(account => new { account.StoreId, account.FarmerProfileId }).IsUnique();

        builder.HasCheck("current_balance", "current_balance >= 0");
    }
}
