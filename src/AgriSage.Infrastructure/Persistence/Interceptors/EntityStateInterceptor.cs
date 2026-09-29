using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgriSage.Infrastructure.Persistence.Interceptors;

// Base of the SaveChanges bookkeeping interceptors (database design §35.15). They only adjust tracked
// entries before EF writes them: no I/O, no nested SaveChanges, no transaction, no Audit Log rows.
// Bulk ExecuteUpdate/ExecuteDelete never reach them (coding rule #64).
public abstract class EntityStateInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    protected abstract void Process(ChangeTracker changeTracker);

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // EF runs its own DetectChanges only after the SavingChanges interceptors.
        context.ChangeTracker.DetectChanges();
        Process(context.ChangeTracker);
    }
}
