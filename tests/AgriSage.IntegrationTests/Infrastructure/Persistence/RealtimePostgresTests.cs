using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Runtime.CompilerServices;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.BackgroundJobs;
using AgriSage.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class RealtimeDbFactAttribute : FactAttribute
{
    public RealtimeDbFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGRISAGE_REALTIME_TEST_CONNECTION_STRING")))
            Skip = "Requires an isolated PostgreSQL database in AGRISAGE_REALTIME_TEST_CONNECTION_STRING.";
    }
}

public sealed class RealtimePostgresTests
{
    [RealtimeDbFact]
    public async Task Durable_worker_fans_out_to_all_instances_and_retries_failed_work()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(token);
        var one = new Probe(); var two = new Probe(); var wake = new RealtimeWakeSignal();
        var services = new ServiceCollection();
        services.AddDbContext<AgriSageDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<IAgriSageDbContext>(sp => sp.GetRequiredService<AgriSageDbContext>());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<INotificationOutboxLock, NotificationOutboxLock>();
        services.AddScoped<NotificationWriter>(); services.AddScoped<NotificationDispatcher>();
        services.AddScoped<CommittedNotificationService>(); services.AddSingleton<IRealtimePublisher>(one);
        await using var provider = services.BuildServiceProvider();
        await using var secondProvider = new ServiceCollection().AddSingleton<IRealtimePublisher>(two).BuildServiceProvider();
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var database = Options.Create(new DatabaseOptions { Host = connection.Host, Port = connection.Port,
            Database = connection.Database, Username = connection.Username, Password = connection.Password, SslMode = SslMode.Disable });
        var jobs = Options.Create(new BackgroundJobOptions { Enabled = true });
        using var listenerOne = new PostgresEventListener(provider.GetRequiredService<IServiceScopeFactory>(), wake, database, jobs, NullLogger<PostgresEventListener>.Instance);
        using var listenerTwo = new PostgresEventListener(secondProvider.GetRequiredService<IServiceScopeFactory>(), wake, database, jobs, NullLogger<PostgresEventListener>.Instance);
        using var worker = new RealtimeOutboxWorker(provider.GetRequiredService<IServiceScopeFactory>(), wake, jobs,
            Options.Create(new RealtimeOptions { RecoverySeconds = 5 }), NullLogger<RealtimeOutboxWorker>.Instance);
        try
        {
            await listenerOne.StartAsync(token); await listenerTwo.StartAsync(token);
            await Task.WhenAll(one.Ready.Task, two.Ready.Task).WaitAsync(TimeSpan.FromSeconds(10), token);
            var user = Guid.NewGuid();
            await fixture.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('notifications', 'user', {user})", token);
            await worker.StartAsync(token);
            await Task.WhenAll(one.Signal.Task, two.Signal.Task).WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(one.Signal.Task.Result, two.Signal.Task.Result);
            Assert.Equal(user, one.Signal.Task.Result.SubjectId);
            var completed = await fixture.Db.RealtimeOutbox.AsNoTracking().SingleAsync(e => e.SubjectId == user, token);
            Assert.NotNull(completed.ProcessedAt);

            var missingMovement = Guid.NewGuid();
            await fixture.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('stock-posted', 'movement', {missingMovement})", token);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await fixture.Db.RealtimeOutbox.AsNoTracking().AnyAsync(e => e.SubjectId == missingMovement && e.Attempts > 0, timeout.Token))
                await Task.Delay(50, timeout.Token);
            var failed = await fixture.Db.RealtimeOutbox.AsNoTracking().SingleAsync(e => e.SubjectId == missingMovement, token);
            Assert.Null(failed.ProcessedAt);
            Assert.Equal("DISPATCH_FAILED", failed.LastErrorCode);
            Assert.True(failed.AvailableAt > failed.CreatedAt);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            await listenerOne.StopAsync(CancellationToken.None);
            await listenerTwo.StopAsync(CancellationToken.None);
        }
    }

    [RealtimeDbFact]
    public async Task Triggers_are_atomic_coalesce_per_transaction_and_notify_only_after_commit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(token);
        var db = fixture.Db;
        await using var listener = new NpgsqlConnection(fixture.ConnectionString);
        await listener.OpenAsync(token);
        await using (var command = new NpgsqlCommand("LISTEN agrisage_work", listener)) await command.ExecuteNonQueryAsync(token);
        var received = false;
        listener.Notification += (_, _) => received = true;
        var id = Guid.NewGuid();
        await using (var tx = await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('notifications', 'user', {id})", token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('notifications', 'user', {id})", token);
            Assert.Equal(1, await db.RealtimeOutbox.CountAsync(token));
            await listener.WaitAsync(100, token);
            Assert.False(received);
            await tx.RollbackAsync(token);
        }
        Assert.Empty(await db.RealtimeOutbox.ToListAsync(token));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('notifications', 'user', {id})", token);
        await listener.WaitAsync(2000, token);
        Assert.True(received);
        Assert.Equal(1, await db.RealtimeOutbox.CountAsync(token));
    }

    [RealtimeDbFact]
    public async Task Claim_skips_locked_work_retry_is_durable_and_notification_changes_are_queued()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(token);
        var db = fixture.Db;
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT agrisage_enqueue('notifications', 'user', {id})", token);
        await using var first = new NpgsqlConnection(fixture.ConnectionString);
        await first.OpenAsync(token);
        await using var tx = await first.BeginTransactionAsync(token);
        var claimSql = "SELECT id FROM realtime_outbox WHERE processed_at IS NULL AND available_at <= now() FOR UPDATE SKIP LOCKED LIMIT 1";
        await using var lockCommand = new NpgsqlCommand(claimSql, first, tx);
        Assert.IsType<Guid>(await lockCommand.ExecuteScalarAsync(token));
        await using (var second = new NpgsqlConnection(fixture.ConnectionString))
        {
            await second.OpenAsync(token);
            await using var other = new NpgsqlCommand(claimSql, second);
            Assert.Null(await other.ExecuteScalarAsync(token));
        }
        await tx.RollbackAsync(token);
        var row = await db.RealtimeOutbox.SingleAsync(token);
        var now = DateTimeOffset.UtcNow;
        row.Retry(now);
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        var saved = await db.RealtimeOutbox.SingleAsync(token);
        Assert.Equal(1, saved.Attempts);
        Assert.Equal("DISPATCH_FAILED", saved.LastErrorCode);
        Assert.True(saved.AvailableAt > now);

        var role = new Role(RoleCode.Farmer, "Farmer");
        var user = new User(role.Id, "Farmer", "hash", "farmer@example.test", null);
        db.AddRange(role, user, new Notification(user.Id, "TEST", "Title", "Message"));
        await db.SaveChangesAsync(token);
        Assert.Equal(1, await db.RealtimeOutbox.CountAsync(e => e.Topic == "notifications" && e.SubjectId == user.Id, token));
    }

    [RealtimeDbFact]
    public async Task Store_audience_obeys_membership_deny_live_inheritance_and_owner_ceiling()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(token);
        var db = fixture.Db;
        var ownerRole = new Role(RoleCode.StoreOwner, "Owner");
        var saleRole = new Role(RoleCode.SalesStaff, "Sale");
        var owner = new User(ownerRole.Id, "Owner", "hash", "owner@example.test", null);
        var sale = new User(saleRole.Id, "Sale", "hash", "sale@example.test", null);
        var outsider = new User(saleRole.Id, "Other store", "hash", "other@example.test", null);
        var store = new Store("REALTIME", "Store", "Address", "Province");
        var member = new StoreMember(store.Id, sale.Id);
        var permission = await db.Permissions.SingleAsync(p => p.Code == "INVENTORY.READ", token);
        db.AddRange(ownerRole, saleRole, owner, sale, outsider, store, member, new StoreMember(store.Id, owner.Id));
        var ownerGrant = new RolePermission(ownerRole.Id, permission.Id, true);
        db.AddRange(ownerGrant, new RolePermission(saleRole.Id, permission.Id, true));
        await db.SaveChangesAsync(token);
        var audience = new RealtimeAudience(db);
        var signal = new RealtimeSignal(Guid.NewGuid(), "inventory", "store", store.Id);
        Assert.Equal(new[] { owner.Id, sale.Id }.Order(), (await audience.RecipientsAsync(signal, token)).Order());
        var deny = new StoreMemberPermission(member.Id, permission.Id, false);
        db.Add(deny); await db.SaveChangesAsync(token);
        Assert.DoesNotContain(sale.Id, await audience.RecipientsAsync(signal, token));
        deny.SetGrant(null); await db.SaveChangesAsync(token);
        Assert.Contains(sale.Id, await audience.RecipientsAsync(signal, token));
        ownerGrant.SetGrant(false); await db.SaveChangesAsync(token);
        Assert.DoesNotContain(sale.Id, await audience.RecipientsAsync(signal, token));
        Assert.DoesNotContain(outsider.Id, await audience.RecipientsAsync(signal, token));
        var changed = await audience.RecipientsAsync(new(Guid.NewGuid(), "permissions", "role", ownerRole.Id), token);
        Assert.Contains(sale.Id, changed);
    }

    [RealtimeDbFact]
    public async Task Diagnosis_events_only_reach_the_owning_farmer_with_current_read_permission()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(token);
        var db = fixture.Db;
        var role = new Role(RoleCode.Farmer, "Farmer");
        var one = new User(role.Id, "Farmer one", "hash", "one@example.test", null);
        var two = new User(role.Id, "Farmer two", "hash", "two@example.test", null);
        var profile = new FarmerProfile(one.Id);
        var diagnosis = new DiagnosisCase(profile.Id, "DG-REALTIME", DateTimeOffset.UtcNow);
        var store = new Store("REALTIME", "Store", "Address", "Province");
        var permission = await db.Permissions.SingleAsync(p => p.Code == "MY_DIAGNOSIS.READ", token);
        var grant = new RolePermission(role.Id, permission.Id, true);
        db.AddRange(role, one, two, profile, diagnosis, store, grant);
        await db.SaveChangesAsync(token);
        var audience = new RealtimeAudience(db);
        var signal = new RealtimeSignal(Guid.NewGuid(), "diagnosis", "case", diagnosis.Id);
        Assert.Equal(new[] { one.Id }, await audience.RecipientsAsync(signal, token));
        grant.SetGrant(false); await db.SaveChangesAsync(token);
        Assert.Empty(await audience.RecipientsAsync(signal, token));
        Assert.Contains(await db.RealtimeOutbox.ToListAsync(token), e => e.Topic == "diagnosis" && e.SubjectId == diagnosis.Id);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required AgriSageDbContext Db { get; init; }
        public required string ConnectionString { get; init; }
        public required string Schema { get; init; }
        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var connectionString = Environment.GetEnvironmentVariable("AGRISAGE_REALTIME_TEST_CONNECTION_STRING")!;
            var schema = "realtime_test_" + Guid.NewGuid().ToString("N");
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(token);
                await using var command = new NpgsqlCommand("CREATE SCHEMA " + schema, connection);
                await command.ExecuteNonQueryAsync(token);
            }
            var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var db = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(settings.ConnectionString).Options);
            // The connection is explicitly isolated; never migrate the user's shared application database.
            var sql = db.GetService<IMigrator>().GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
            await db.Database.ExecuteSqlRawAsync(sql, token);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM realtime_outbox", token);
            return new() { Db = db, Schema = schema, ConnectionString = settings.ConnectionString };
        }
        public async ValueTask DisposeAsync()
        {
            await Db.Database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("DROP SCHEMA " + Schema + " CASCADE", (NpgsqlConnection)Db.Database.GetDbConnection());
            await command.ExecuteNonQueryAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class Probe : IRealtimePublisher
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<RealtimeSignal> Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task PublishAsync(RealtimeSignal signal, CancellationToken token) { Signal.TrySetResult(signal); return Task.CompletedTask; }
        public Task ResynchronizeAsync(CancellationToken token) { Ready.TrySetResult(); return Task.CompletedTask; }
    }
}
