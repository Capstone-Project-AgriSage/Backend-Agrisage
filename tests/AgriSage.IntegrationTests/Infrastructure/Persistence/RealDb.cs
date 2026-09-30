using System.Runtime.CompilerServices;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Opt-in tests against the real PostgreSQL development database (Supabase agrisage-dev): set AGRISAGE_DB_TESTS=1.
// Connection = the committed `Database` section + the secret Database:Password (User Secrets / Database__Password).
// Every test runs inside one transaction that is always rolled back, so the database is left unchanged.
// [Fact] that is skipped unless AGRISAGE_DB_TESTS=1 (xUnit v3 needs the Skip message with conditional skips).
public sealed class RealDbFactAttribute : FactAttribute
{
    public RealDbFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!RealDb.Enabled)
        {
            Skip = RealDb.SkipReason;
        }
    }
}

internal static class RealDb
{
    public static bool Enabled => Environment.GetEnvironmentVariable("AGRISAGE_DB_TESTS") == "1";

    public static string SkipReason => "Real PostgreSQL tests; set AGRISAGE_DB_TESTS=1 (and Database:Password) to run.";

    public static string ConnectionString()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AgriSage.sln")))
        {
            root = root.Parent;
        }

        var api = Path.Combine(root!.FullName, "src", "AgriSage.Api");
        var secrets = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", "6846d3dd-c916-4f07-a77a-fc69a19797d2", "secrets.json");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"))
            .AddJsonFile(Path.Combine(api, "appsettings.Development.json"))
            .AddJsonFile(secrets, optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()!.BuildConnectionString()
            ?? throw new InvalidOperationException("No Database:Host configured.");
    }

    public static async Task<PostgresException> ExpectViolationAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(action);

        return exception as PostgresException ?? Assert.IsType<PostgresException>(exception.InnerException);
    }

    public sealed class Session : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;

        private Session(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public MutableUser CurrentUser { get; } = new();

        public static async Task<Session> StartAsync()
        {
            var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();

            return new Session(connection, await connection.BeginTransactionAsync());
        }

        // Contexts share the connection and its transaction, so they see each other's uncommitted rows —
        // which lets one test simulate two concurrent writers.
        public AgriSageDbContext NewContext()
        {
            var clock = new Clock();
            var context = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>()
                .UseNpgsql(_connection)
                .AddInterceptors(
                    new SoftDeleteInterceptor(CurrentUser, clock),
                    new AuditableEntityInterceptor(clock),
                    new ConcurrencyVersionInterceptor())
                .Options);

            context.Database.UseTransaction(_transaction);

            return context;
        }

        public async Task<(User User, Store Store, FarmerProfile Farmer)> SeedAsync(AgriSageDbContext context)
        {
            var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == RoleCode.Farmer);
            var newRole = role is null ? new Role(RoleCode.Farmer, "Farmer") : null;
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var user = new User((role ?? newRole!).Id, "Test User", "hash", $"{suffix}@example.test", null);
            var store = new Store($"T{suffix}", "Test store", "1 Test Street", "Test");
            var farmer = new FarmerProfile(user.Id);

            if (newRole is not null)
            {
                context.Add(newRole);
            }

            context.AddRange(user, store, farmer);
            await context.SaveChangesAsync();
            CurrentUser.UserId = user.Id;

            return (user, store, farmer);
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class MutableUser : ICurrentUserService
    {
        public Guid? UserId { get; set; }

        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
