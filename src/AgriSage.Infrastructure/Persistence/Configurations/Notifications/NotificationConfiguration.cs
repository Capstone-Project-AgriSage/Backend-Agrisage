using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Notifications.Enums;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Notifications;

// Table 66: notifications.
internal sealed class NotificationConfiguration : EntityConfiguration<Notification>
{
    protected override string TableName => "notifications";

    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(notification => notification.NotificationType).HasMaxLength(50);
        builder.Property(notification => notification.Title).HasMaxLength(255);
        builder.Property(notification => notification.Message).HasMaxLength(2000);
        builder.Property(notification => notification.Data).IsJson();
        builder.Property(notification => notification.DeduplicationKey).HasMaxLength(200);
        builder.HasIndex(notification => new { notification.UserId, notification.DeduplicationKey })
            .IsUnique().HasFilter("deduplication_key IS NOT NULL");
        builder.Property(notification => notification.Status).HasMaxLength(20).HasDbDefault(NotificationStatus.Unread);

        builder.HasReference<Notification, User>(notification => notification.UserId);

        builder.HasIndex(notification => new { notification.UserId, notification.Status, notification.CreatedAt })
            .IsDescending(false, false, true);
    }
}
