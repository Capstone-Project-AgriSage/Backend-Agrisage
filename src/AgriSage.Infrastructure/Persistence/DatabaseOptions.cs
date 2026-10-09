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

    // Supabase's session-mode pooler (port 5432) lets the whole project hold only 15 client connections, shared by
    // everyone who runs the API, while Npgsql's own default would let one process open 100 and take them all (every
    // request then fails with EMAXCONNSESSION). A small pool queues the odd extra request instead, and idle
    // connections go back to the pooler quickly. Raise these for a database that is not behind such a pooler.
    public int MaxPoolSize { get; init; } = 5;

    public int MinPoolSize { get; init; } = 0;

    // Seconds an unused connection stays open before it is closed.
    public int ConnectionIdleLifetimeSeconds { get; init; } = 20;

    // Final Npgsql connection string, or null when no host is configured (nothing connects at startup;
    // the design-time/test model is built without a database). A missing password is left for PostgreSQL
    // to reject on first connection rather than failing startup.
    public string? BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return null;
        }

        var maxPoolSize = Math.Max(1, MaxPoolSize);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            SslMode = SslMode,
            MaxPoolSize = maxPoolSize,
            MinPoolSize = Math.Clamp(MinPoolSize, 0, maxPoolSize),
            ConnectionIdleLifetime = Math.Max(1, ConnectionIdleLifetimeSeconds)
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
