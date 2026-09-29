using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgriSage.Infrastructure.Persistence.Interceptors;

// Turns EF Remove() of a soft-deletable entity into an UPDATE of deleted_at / deleted_by (database design
// §0.6, §35.15). Runs first, so the soft-deleted row also gets updated_at and a version increment.
// The Domain decides whether the delete is allowed: aggregate children, confirmed/posted records and
// already-deleted rows throw a DomainException. Domain-initiated soft deletes (MarkDeleted,
// root.RemoveItem) are already Modified and keep the values the Application passed.
public sealed class SoftDeleteInterceptor(ICurrentUserService currentUser, IDateTimeProvider clock)
    : EntityStateInterceptor
{
    protected override void Process(ChangeTracker changeTracker)
    {
        var deletedEntries = changeTracker.Entries<SoftDeletableEntity>()
            .Where(entry => entry.State == EntityState.Deleted)
            .ToList();

        if (deletedEntries.Count == 0)
        {
            return;
        }

        var deletedBy = currentUser.UserId;
        var deletedAt = clock.UtcNow;

        foreach (var entry in deletedEntries)
        {
            // Domain rules run while the entry is still Deleted, so a rejected delete leaves it untouched.
            entry.Entity.MarkDeleted(deletedBy, deletedAt);

            // Deleted → Modified directly keeps the original values, including the concurrency version
            // checked by the UPDATE (never via Unchanged). Only columns that actually changed are written.
            entry.State = EntityState.Modified;

            foreach (var property in entry.Properties)
            {
                if (property.IsModified && Equals(property.OriginalValue, property.CurrentValue))
                {
                    property.IsModified = false;
                }
            }
        }
    }
}
