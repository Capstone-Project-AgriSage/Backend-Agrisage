using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// debt_follow_ups (DATABASE_DESIGN §35.24, F3.23): reminders made outside the system, append-only through the API.
// The channel and outcome checks come from the enum convention.
internal sealed class DebtFollowUpConfiguration : EntityConfiguration<DebtFollowUp>
{
    protected override string TableName => "debt_follow_ups";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtFollowUp> builder)
    {
        builder.Property(followUp => followUp.Channel).HasMaxLength(20);
        builder.Property(followUp => followUp.Outcome).HasMaxLength(30);
        builder.Property(followUp => followUp.Note).HasMaxLength(DebtFollowUp.MaxNoteLength);
        builder.Property(followUp => followUp.PromisedAmount).IsMoney();
        builder.Property(followUp => followUp.OutstandingAmount).IsMoney();
        builder.Property(followUp => followUp.OverdueAmount).IsMoney();

        builder.HasReference<DebtFollowUp, DebtAccount>(followUp => followUp.DebtAccountId);
        builder.HasUserReference(followUp => followUp.CreatedBy);

        builder.HasCheck("promise",
            "(outcome = 'PROMISED' AND promised_date IS NOT NULL) "
            + "OR (outcome <> 'PROMISED' AND promised_date IS NULL AND promised_amount IS NULL)");
        builder.HasCheck("promised_amount", "promised_amount IS NULL OR promised_amount > 0");
        builder.HasCheck("amounts", "outstanding_amount > 0 AND overdue_amount >= 0 AND overdue_amount <= outstanding_amount");

        builder.HasIndex(followUp => new { followUp.DebtAccountId, followUp.ContactedAt }).IsDescending(false, true);
    }
}
