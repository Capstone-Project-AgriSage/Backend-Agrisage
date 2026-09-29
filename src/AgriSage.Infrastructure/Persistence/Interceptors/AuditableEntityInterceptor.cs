using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Audit.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgriSage.Infrastructure.Persistence.Interceptors;

// System-managed created_at / updated_at (database design §0.7, §35.15); caller values are overwritten.
// audit_logs has neither column and is append-only (§67, §35.13): updating or deleting one is rejected.
public sealed class AuditableEntityInterceptor(IDateTimeProvider clock) : EntityStateInterceptor
{
    protected override void Process(ChangeTracker changeTracker)
    {
        EnsureAuditLogsAreAppendOnly(changeTracker);

        var now = clock.UtcNow;

        foreach (var entry in changeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(entity => entity.CreatedAt).CurrentValue = now;
                    entry.Property(entity => entity.UpdatedAt).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    var createdAt = entry.Property(entity => entity.CreatedAt);
                    createdAt.CurrentValue = createdAt.OriginalValue;
                    createdAt.IsModified = false;

                    entry.Property(entity => entity.UpdatedAt).CurrentValue = now;
                    break;
            }
        }
    }

    private static void EnsureAuditLogsAreAppendOnly(ChangeTracker changeTracker)
    {
        var changed = changeTracker.Entries<AuditLog>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (changed is not null)
        {
            throw new InvalidOperationException(
                $"Audit log '{changed.Entity.Id}' is append-only; it cannot be {changed.State.ToString().ToLowerInvariant()}.");
        }
    }
}
