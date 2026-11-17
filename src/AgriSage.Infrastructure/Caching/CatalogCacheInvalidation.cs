using System.Data.Common;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgriSage.Infrastructure.Caching;

// Shared by both interceptors within one DbContext scope. Invalidate only after commit,
// including writes made by maintenance/import code through the normal EF pipeline.
public sealed class CatalogCacheInvalidation(ICatalogCacheBackend backend)
{
    private bool pending;
    public void Capture(DbContext? context)
    {
        if (context is null) return;
        pending |= context.ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            e.Entity is Category or Brand or ActiveIngredient or Unit or Product
                or ProductPackaging or ProductActiveIngredient or Disease or DiseaseTreatment);
    }
    public void Reset() => pending = false;
    public async Task FlushAsync()
    {
        if (!pending) return;
        pending = false;
        await backend.InvalidateAsync().ConfigureAwait(false);
    }
}

public sealed class CatalogCacheSaveInterceptor(CatalogCacheInvalidation invalidation) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        invalidation.Capture(eventData.Context);
        return result;
    }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        invalidation.Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
            invalidation.FlushAsync().GetAwaiter().GetResult();
        return result;
    }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
            await invalidation.FlushAsync().ConfigureAwait(false);
        return result;
    }
}

public sealed class CatalogCacheTransactionInterceptor(CatalogCacheInvalidation invalidation) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        invalidation.FlushAsync().GetAwaiter().GetResult();
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) => invalidation.FlushAsync();
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => invalidation.Reset();
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        invalidation.Reset();
        return Task.CompletedTask;
    }
}
