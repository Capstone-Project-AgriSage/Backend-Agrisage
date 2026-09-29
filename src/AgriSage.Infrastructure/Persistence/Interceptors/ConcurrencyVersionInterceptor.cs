using AgriSage.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgriSage.Infrastructure.Persistence.Interceptors;

// Optimistic concurrency version of the four `version` tables (database design §0.8, §35.15).
// Insert → 0. Every persisted update of the row, soft delete included → original + 1; the UPDATE is
// checked against the original value, so a conflicting write raises DbUpdateConcurrencyException.
// Unchanged rows keep their version; changes to child rows do not bump the root. No retry here.
public sealed class ConcurrencyVersionInterceptor : EntityStateInterceptor
{
    protected override void Process(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries<IHasConcurrencyVersion>())
        {
            var version = entry.Property<long>(nameof(IHasConcurrencyVersion.Version));

            switch (entry.State)
            {
                case EntityState.Added:
                    version.CurrentValue = 0;
                    break;

                case EntityState.Modified:
                    version.CurrentValue = version.OriginalValue + 1;
                    break;
            }
        }
    }
}
