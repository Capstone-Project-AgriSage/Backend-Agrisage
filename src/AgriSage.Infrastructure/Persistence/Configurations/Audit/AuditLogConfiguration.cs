using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Audit;

// Table 67: audit_logs. Append-only; no audit/soft-delete columns (BaseEntity).
internal sealed class AuditLogConfiguration : EntityConfiguration<AuditLog>
{
    protected override string TableName => "audit_logs";

    protected override void ConfigureEntity(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(log => log.Action).HasMaxLength(100);
        builder.Property(log => log.EntityType).HasMaxLength(100);
        builder.Property(log => log.OldValues).IsJson();
        builder.Property(log => log.NewValues).IsJson();
        builder.Property(log => log.Reason).HasMaxLength(1000);
        builder.Property(log => log.IpAddress).HasMaxLength(64);
        builder.Property(log => log.UserAgent).HasMaxLength(1000);
        builder.Property(log => log.CorrelationId).HasMaxLength(100);

        builder.HasReference<AuditLog, Store>(log => log.StoreId);
        builder.HasReference<AuditLog, User>(log => log.ActorUserId);

        builder.HasIndex(log => new { log.EntityType, log.EntityId, log.OccurredAt }).IsDescending(false, false, true);
        builder.HasIndex(log => new { log.ActorUserId, log.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(log => log.CorrelationId);
        builder.HasIndex(log => log.OccurredAt).IsDescending();
    }
}
