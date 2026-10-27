using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.Infrastructure.Persistence;

public sealed class NpgsqlErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public bool IsUnavailable(Exception exception)
    {
        for (Exception? error = exception; error is not null; error = error.InnerException)
        {
            if (error is PostgresException postgres)
            {
                if (postgres.SqlState is PostgresErrorCodes.TooManyConnections or PostgresErrorCodes.CannotConnectNow
                    || postgres.SqlState.StartsWith("08", StringComparison.Ordinal)) return true;
                // Supavisor reports session-pool capacity with XX000, not PostgreSQL's 53300.
                // Do not classify every XX000 (including unrelated internal errors) as capacity exhaustion.
                if (postgres.SqlState == PostgresErrorCodes.InternalError
                    && (postgres.MessageText.Contains("EMAXCONNSESSION", StringComparison.OrdinalIgnoreCase)
                        || postgres.MessageText.Contains("max clients reached", StringComparison.OrdinalIgnoreCase))) return true;
            }
            else if (error is NpgsqlException && error.InnerException is TimeoutException or IOException or System.Net.Sockets.SocketException)
                return true;
        }
        return false;
    }
}
