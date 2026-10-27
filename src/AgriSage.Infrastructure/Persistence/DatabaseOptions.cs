using Npgsql;

namespace AgriSage.Infrastructure.Persistence;

// PostgreSQL/Supabase connection settings ("Database" section). Everything except the password is
// non-secret and committed per environment (appsettings.{Environment}.json). The password comes only from
// User Secrets (`Database:Password`) or the `Database__Password` environment variable.
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string? Host { get; init; }

    public int Port { get; init; } = 5432;

    public string? Database { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public SslMode SslMode { get; init; } = SslMode.Require;

    // Per API process/connection string. Leave room for LISTEN (1), a scheduler lease (1),
    // other API instances and external tools on small Supabase session pools.
    public int MaximumPoolSize { get; init; } = 4;
    public int MinimumPoolSize { get; init; }
    public int ConnectionIdleLifetimeSeconds { get; init; } = 30;
    public int ConnectionPruningIntervalSeconds { get; init; } = 5;
    public int ConnectionTimeoutSeconds { get; init; } = 15;

    // Final Npgsql connection string, or null when no host is configured (nothing connects at startup;
    // the design-time/test model is built without a database). A missing password is left for PostgreSQL
    // to reject on first connection rather than failing startup.
    public string? BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return null;
        }

        if (MaximumPoolSize < 1 || MinimumPoolSize < 0 || MinimumPoolSize > MaximumPoolSize
            || ConnectionPruningIntervalSeconds < 1 || ConnectionIdleLifetimeSeconds < ConnectionPruningIntervalSeconds
            || ConnectionTimeoutSeconds < 1)
            throw new InvalidOperationException("Database connection-pool settings are invalid.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            SslMode = SslMode,
            Pooling = true,
            MaxPoolSize = MaximumPoolSize,
            MinPoolSize = MinimumPoolSize,
            ConnectionIdleLifetime = ConnectionIdleLifetimeSeconds,
            ConnectionPruningInterval = ConnectionPruningIntervalSeconds,
            Timeout = ConnectionTimeoutSeconds,
            ApplicationName = "AgriSage.Api"
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
