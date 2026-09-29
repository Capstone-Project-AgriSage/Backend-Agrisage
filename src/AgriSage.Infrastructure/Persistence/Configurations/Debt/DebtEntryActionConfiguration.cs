using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// Table 50: debt_entry_actions.
internal sealed class DebtEntryActionConfiguration : EntityConfiguration<DebtEntryAction>
{
    protected override string TableName => "debt_entry_actions";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtEntryAction> builder)
    {
        builder.Property(action => action.ActionType).HasMaxLength(30);
        builder.Property(action => action.PreviousOutstandingAmount).IsMoney();
        builder.Property(action => action.AdjustmentAmount).IsMoney();
        builder.Property(action => action.ResultingOutstandingAmount).IsMoney();
        builder.Property(action => action.Reason).HasMaxLength(1000);

        builder.HasUserReference(action => action.CreatedBy);

        builder.HasIndex(action => new { action.DebtEntryId, action.CreatedAt }).IsDescending(false, true);
    }
}
