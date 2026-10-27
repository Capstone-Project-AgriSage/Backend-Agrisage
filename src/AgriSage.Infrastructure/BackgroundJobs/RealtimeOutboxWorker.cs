using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Notifications;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriSage.Infrastructure.BackgroundJobs;

public sealed class RealtimeOutboxWorker(IServiceScopeFactory scopes, RealtimeWakeSignal wake,
    IOptions<BackgroundJobOptions> jobs, IOptions<RealtimeOptions> options, ILogger<RealtimeOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (!jobs.Value.Enabled) return;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromSeconds(jobs.Value.JobTimeoutSeconds));
                // Existing business-notification dispatcher retains its row locks, recipient policy and dedup keys.
                await DrainNotificationsAsync(budget.Token);
                var full = true;
                for (var i = 0; i < options.Value.BatchSize; i++)
                    if (!await DispatchOneAsync(budget.Token)) { full = false; break; }
                if (full) { wake.Wake(); await Task.Yield(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning("Event outbox processing failed ({ErrorType}); recovery will retry.", e.GetType().Name); }
            try { await wake.WaitAsync(TimeSpan.FromSeconds(options.Value.RecoverySeconds), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }

    private async Task DrainNotificationsAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
        var ids = await dispatcher.DueAsync(options.Value.BatchSize, token);
        foreach (var id in ids)
        {
            try
            {
                await using var dispatch = scopes.CreateAsyncScope();
                await dispatch.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(id, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                await using var retry = scopes.CreateAsyncScope();
                await retry.ServiceProvider.GetRequiredService<NotificationDispatcher>().RetryAsync(id, token);
                logger.LogWarning("Notification dispatch failed ({ErrorType}); retry scheduled.", e.GetType().Name);
            }
        }
        if (ids.Count == options.Value.BatchSize) wake.Wake();
    }

    private async Task<bool> DispatchOneAsync(CancellationToken token)
    {
        Guid? id = null;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            // Competing API instances claim different jobs, and a crash releases the row lock automatically.
            id = await ClaimAsync(db, token);
            if (id is null) return false;
            var message = await db.RealtimeOutbox.SingleAsync(e => e.Id == id, token);
            var notifications = scope.ServiceProvider.GetRequiredService<CommittedNotificationService>();
            if (message.Topic == "stock-posted") await notifications.StockAsync(1, token, message.SubjectId);
            else
            {
                if (message.Topic == "diagnosis") await notifications.DiagnosisAsync(1, token, message.SubjectId);
                var payload = JsonSerializer.Serialize(new RealtimeSignal(message.Id, message.Topic, message.Scope, message.SubjectId));
                // NOTIFY is delivered only at commit, atomically with processed_at and business notifications.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify('agrisage_realtime', {payload})", token);
            }
            message.Complete(clock.UtcNow);
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (id is not null)
        {
            await using var retry = scopes.CreateAsyncScope();
            var db = retry.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM realtime_outbox WHERE id = {id} FOR UPDATE", token);
            var message = await db.RealtimeOutbox.SingleOrDefaultAsync(e => e.Id == id && e.ProcessedAt == null, token);
            if (message is not null)
            {
                message.Retry(retry.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow);
                await db.SaveChangesAsync(token);
            }
            await tx.CommitAsync(token);
            logger.LogWarning("Realtime event dispatch failed ({ErrorType}); retry scheduled.", e.GetType().Name);
            return true;
        }
    }

    private static async Task<Guid?> ClaimAsync(AgriSageDbContext db, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM realtime_outbox WHERE processed_at IS NULL AND deleted_at IS NULL AND available_at <= now()
            ORDER BY available_at, id FOR UPDATE SKIP LOCKED LIMIT 1
            """, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
        return await command.ExecuteScalarAsync(token) is Guid id ? id : null;
    }
}
