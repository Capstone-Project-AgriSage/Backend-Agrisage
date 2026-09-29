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
}
