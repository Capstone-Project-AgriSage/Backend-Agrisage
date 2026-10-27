using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class RealtimeOutboxMessageConfiguration : EntityConfiguration<RealtimeOutboxMessage>
{
    protected override string TableName => "realtime_outbox";
    protected override void ConfigureEntity(EntityTypeBuilder<RealtimeOutboxMessage> b)
    {
        b.Property(x => x.Topic).HasMaxLength(30);
        b.Property(x => x.Scope).HasMaxLength(20);
        b.Property(x => x.LastErrorCode).HasMaxLength(100);
        b.HasIndex(x => new { x.AvailableAt, x.Id }).HasFilter("processed_at IS NULL AND deleted_at IS NULL");
        b.HasIndex(x => new { x.TransactionId, x.Topic, x.Scope, x.SubjectId }).IsUnique();
        b.HasCheck("attempts", "attempts >= 0");
        b.HasCheck("topic", "topic IN ('notifications', 'permissions', 'inventory', 'diagnosis', 'stock-posted')");
        b.HasCheck("scope", "scope IN ('user', 'role', 'member', 'store', 'all', 'case', 'movement')");
    }
}
