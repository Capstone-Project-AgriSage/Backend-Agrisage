using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.Infrastructure.BackgroundJobs;

// Session advisory lock on a dedicated unpooled connection. Closing it releases the lock on crash/cancellation.
public sealed class PostgresBackgroundJobLock(AgriSageDbContext context) : IBackgroundJobLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(BackgroundTask task, CancellationToken token)
    {
        var settings = new NpgsqlConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString) { Pooling = false };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(token);
            var key = 7_316_070_000L + (int)task;
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(token) is true) return new Lease(connection);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
