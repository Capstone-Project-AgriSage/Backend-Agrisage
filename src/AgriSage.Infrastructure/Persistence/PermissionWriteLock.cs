using AgriSage.Application.Features.Permissions;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

public sealed class PermissionWriteLock(AgriSageDbContext db) : IPermissionWriteLock
{
    public async Task AcquireAsync(CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Permission writes require a transaction.");
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(704400901)", token);
    }
}
