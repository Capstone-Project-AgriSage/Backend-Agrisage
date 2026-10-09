using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.Infrastructure.Persistence;

public sealed class NpgsqlErrorClassifier : IDatabaseErrorClassifier
{
    // Supabase's pooler (Supavisor) answers XX000 "(EMAXCONNSESSION) max clients reached in session mode" when its
    // client limit is full; plain PostgreSQL answers 53300 (too_many_connections).
    private const string PoolerLimitMarker = "max clients reached";
    private const string PoolerLimitCode = "EMAXCONN";
    private const string LocalPoolExhaustedMarker = "connection pool has been exhausted";

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public bool IsConnectionUnavailable(Exception exception)
    {
        // The error may arrive wrapped (a failed SaveChanges is a DbUpdateException around it).
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && IsConnectionLimit(postgres))
            {
                return true;
            }

            if (current is InvalidOperationException or NpgsqlException
                && current.Message.Contains(LocalPoolExhaustedMarker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConnectionLimit(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.TooManyConnections
        || exception.MessageText.Contains(PoolerLimitMarker, StringComparison.OrdinalIgnoreCase)
        || exception.MessageText.Contains(PoolerLimitCode, StringComparison.OrdinalIgnoreCase);
}
