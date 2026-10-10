using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Persistence.Seed;

public sealed class DatabaseSeeder(AgriSageDbContext context, IOptions<SeedStoreOptions> storeOptions)
{
    public sealed record Result(int Roles, int Units, int Diseases, int Stores)
    {
        public int Total => Roles + Units + Diseases + Stores;
    }

    public async Task<Result> SeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        storeOptions.Value.Validate();
        if (context.ChangeTracker.HasChanges())
        {
            throw new ReferenceSeedException("Reference seed requires a context without pending changes.");
        }

        // The command owns its transaction. Rollback-only tests may supply an existing transaction:
        // a savepoint isolates a failed invocation without committing or nesting that transaction.
        var existingTransaction = context.Database.CurrentTransaction;
        await using var ownedTransaction = existingTransaction is null
            ? await context.BeginTransactionAsync(cancellationToken)
            : null;
        var transaction = existingTransaction ?? ownedTransaction!;
        var savepoint = $"reference_seed_{Guid.NewGuid():N}";
        if (existingTransaction is not null)
        {
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        }

        try
        {
            var roles = await RoleSeeder.StageAsync(context, cancellationToken);
            await PermissionSeeder.StageAsync(context, cancellationToken);
            var units = await UnitSeeder.StageAsync(context, cancellationToken);
            var diseases = await DiseaseSeeder.StageAsync(context, cancellationToken);
            var stores = await StoreSeeder.StageAsync(context, storeOptions.Value, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            if (ownedTransaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            }

            return new Result(roles, units, diseases, stores);
        }
        catch
        {
            // Cleanup must still run when the caller's token has been cancelled.
            if (ownedTransaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }

            context.ChangeTracker.Clear();
            throw;
        }
    }
}
