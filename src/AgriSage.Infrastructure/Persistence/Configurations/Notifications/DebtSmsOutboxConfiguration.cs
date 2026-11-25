using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class DebtSmsOutboxConfiguration : EntityConfiguration<DebtSmsOutbox>
{
    protected override string TableName => "debt_sms_outbox";

    protected override void ConfigureEntity(EntityTypeBuilder<DebtSmsOutbox> builder)
    {
        builder.HasReference<DebtSmsOutbox, DebtAccount>(x => x.DebtAccountId);
        builder.Property(x => x.DeduplicationKey).HasMaxLength(200);
        builder.HasIndex(x => x.DeduplicationKey).IsUnique();
        builder.Property(x => x.PhoneNumber).HasMaxLength(20);
        builder.Property(x => x.Message).HasMaxLength(500);
        builder.Property(x => x.Milestone).HasMaxLength(2);
        builder.Property(x => x.Attempts).HasDbDefault(0);
        builder.Property(x => x.LastErrorCode).HasMaxLength(100);
        builder.HasIndex(x => x.AvailableAt).HasFilter("sent_at IS NULL AND cancelled_at IS NULL AND deleted_at IS NULL");
        builder.HasCheck("attempts", "attempts >= 0");
    }
}
