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

    // Final Npgsql connection string, or null when no host is configured (nothing connects at startup;
    // the design-time/test model is built without a database). A missing password is left for PostgreSQL
    // to reject on first connection rather than failing startup.
    public string? BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            SslMode = SslMode
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
