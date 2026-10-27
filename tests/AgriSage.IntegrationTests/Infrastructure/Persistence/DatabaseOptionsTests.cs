using AgriSage.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class DatabaseOptionsTests
{
    [Fact]
    public void Without_a_host_there_is_no_connection_string()
    {
        Assert.Null(new DatabaseOptions().BuildConnectionString());
    }

    [Fact]
    public void Connection_string_is_built_from_the_database_section_and_the_secret_password()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = "db.example.supabase.co",
                ["Database:Port"] = "5432",
                ["Database:Database"] = "postgres",
                ["Database:Username"] = "postgres",
                ["Database:SslMode"] = "Require",
                ["Database:Password"] = "test-only-password"
            })
            .Build();

        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()!;
        var built = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        Assert.Equal("db.example.supabase.co", built.Host);
        Assert.Equal(5432, built.Port);
        Assert.Equal("postgres", built.Database);
        Assert.Equal("postgres", built.Username);
        Assert.Equal(SslMode.Require, built.SslMode);
        Assert.Equal("test-only-password", built.Password);
    }

    [Fact]
    public void Missing_password_is_left_out_instead_of_failing_startup()
    {
        var options = new DatabaseOptions { Host = "db.example.supabase.co", Database = "postgres", Username = "postgres" };

        var built = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        Assert.Null(built.Password);
        Assert.Equal(SslMode.Require, built.SslMode);
    }

    [Fact]
    public void Defaults_bound_sessions_and_release_idle_capacity_for_other_clients()
    {
        var built = new NpgsqlConnectionStringBuilder(new DatabaseOptions { Host = "localhost" }.BuildConnectionString());
        Assert.True(built.Pooling);
        Assert.Equal(4, built.MaxPoolSize);
        Assert.Equal(0, built.MinPoolSize);
        Assert.Equal(30, built.ConnectionIdleLifetime);
        Assert.Equal(5, built.ConnectionPruningInterval);
        Assert.Equal(15, built.Timeout);
        Assert.Equal("AgriSage.Api", built.ApplicationName);
    }

    [Fact]
    public void Deployments_can_set_their_own_pool_budget()
    {
        var built = new NpgsqlConnectionStringBuilder(new DatabaseOptions
        {
            Host = "localhost", MaximumPoolSize = 2, MinimumPoolSize = 0,
            ConnectionIdleLifetimeSeconds = 20, ConnectionPruningIntervalSeconds = 4, ConnectionTimeoutSeconds = 7
        }.BuildConnectionString());
        Assert.Equal(2, built.MaxPoolSize);
        Assert.Equal(20, built.ConnectionIdleLifetime);
        Assert.Equal(4, built.ConnectionPruningInterval);
        Assert.Equal(7, built.Timeout);
    }

    [Fact]
    public void An_invalid_budget_is_rejected_before_opening_any_connection()
    {
        Assert.Throws<InvalidOperationException>(() => new DatabaseOptions { Host = "localhost", MaximumPoolSize = 0 }.BuildConnectionString());
        Assert.Throws<InvalidOperationException>(() => new DatabaseOptions { Host = "localhost", MaximumPoolSize = 2, MinimumPoolSize = 3 }.BuildConnectionString());
        Assert.Throws<InvalidOperationException>(() => new DatabaseOptions { Host = "localhost", ConnectionIdleLifetimeSeconds = 1, ConnectionPruningIntervalSeconds = 5 }.BuildConnectionString());
    }
}
