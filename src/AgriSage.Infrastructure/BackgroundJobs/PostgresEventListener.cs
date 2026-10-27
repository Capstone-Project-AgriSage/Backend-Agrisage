using System.Text.Json;
using AgriSage.Application.Features.Notifications;
using AgriSage.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriSage.Infrastructure.BackgroundJobs;

// Each API instance listens, so PostgreSQL fans invalidations out to every instance without a SignalR backplane.
public sealed class PostgresEventListener(IServiceScopeFactory scopes, RealtimeWakeSignal wake,
    IOptions<DatabaseOptions> database, IOptions<BackgroundJobOptions> jobs, ILogger<PostgresEventListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var configured = database.Value.BuildConnectionString();
        if (!jobs.Value.Enabled || configured is null) return;
        var settings = new NpgsqlConnectionStringBuilder(configured) { Pooling = false, KeepAlive = 30 };
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(settings.ConnectionString);
                var pending = new Queue<string>();
                var resynchronize = false;
                connection.Notification += (_, e) =>
                {
                    if (e.Channel == "agrisage_work") wake.Wake();
                    else if (!resynchronize && pending.Count < 1000) pending.Enqueue(e.Payload);
                    else { pending.Clear(); resynchronize = true; }
                };
                await connection.OpenAsync(token);
                await using (var listen = new NpgsqlCommand("LISTEN agrisage_work; LISTEN agrisage_realtime", connection))
                    await listen.ExecuteNonQueryAsync(token);
                // Re-sync all local sockets after a listener interruption (NOTIFY itself is not a durable log).
                await using (var scope = scopes.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().ResynchronizeAsync(token);
                wake.Wake();
                while (!token.IsCancellationRequested)
                {
                    if (resynchronize)
                    {
                        await using var recovery = scopes.CreateAsyncScope();
                        await recovery.ServiceProvider.GetRequiredService<IRealtimePublisher>().ResynchronizeAsync(token);
                        resynchronize = false;
                    }
                    while (pending.TryDequeue(out var payload))
                    {
                        var signal = JsonSerializer.Deserialize<RealtimeSignal>(payload)
                            ?? throw new JsonException("Invalid realtime signal.");
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().PublishAsync(signal, token);
                        failures = 0;
                    }
                    await connection.WaitAsync(token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                logger.LogWarning("PostgreSQL event listener interrupted ({ErrorType}); reconnecting.", e.GetType().Name);
                var delay = Math.Min(60, 5 * Math.Pow(2, Math.Min(failures++, 4)));
                try { await Task.Delay(TimeSpan.FromSeconds(delay), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            }
        }
    }
}
