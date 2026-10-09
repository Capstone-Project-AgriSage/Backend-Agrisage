using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationOutboxConfiguration : EntityConfiguration<NotificationOutbox>
{
    protected override string TableName => "notification_outbox";
    protected override void ConfigureEntity(EntityTypeBuilder<NotificationOutbox> builder)
    {
        builder.HasReference<NotificationOutbox, AuditLog>(e => e.AuditLogId);
        builder.HasIndex(e => e.AuditLogId).IsUnique();
        builder.HasIndex(e => e.AvailableAt).HasFilter("processed_at IS NULL AND deleted_at IS NULL");
        builder.Property(e => e.Attempts).HasDbDefault(0);
        builder.Property(e => e.LastErrorCode).HasMaxLength(100);
        builder.HasCheck("attempts", "attempts >= 0");
    }
}
