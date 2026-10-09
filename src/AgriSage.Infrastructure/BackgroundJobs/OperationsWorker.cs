using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.BackgroundJobs;

// Scheduling/transport only. Every use case runs in a fresh scope with an anonymous system actor.
public sealed class OperationsWorker(IServiceScopeFactory scopes, IOptions<BackgroundJobOptions> options,
    TimeProvider time, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled) return;
        var next = new Dictionary<BackgroundTask, DateTimeOffset>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.PollSeconds), time);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var task in Enum.GetValues<BackgroundTask>())
            {
                if (!Enabled(task, o) || next.GetValueOrDefault(task) > time.GetUtcNow()) continue;
                try
                {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    budget.CancelAfter(TimeSpan.FromSeconds(o.JobTimeoutSeconds));
                    await using var scope = scopes.CreateAsyncScope();
                    await using var lease = await scope.ServiceProvider.GetRequiredService<IBackgroundJobLock>().TryAcquireAsync(task, budget.Token);
                    if (lease is not null) await RunAsync(task, o, budget.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception e) { logger.LogWarning("Background task {Task} failed ({ErrorType}); it will retry.", task, e.GetType().Name); }
                next[task] = time.GetUtcNow().AddSeconds(Interval(task, o));
            }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task RunAsync(BackgroundTask task, BackgroundJobOptions o, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        switch (task)
        {
            case BackgroundTask.Notifications:
                var ids = await services.GetRequiredService<NotificationDispatcher>().DueAsync(o.BatchSize, token);
                foreach (var id in ids)
                {
                    try
                    {
                        await using var dispatch = scopes.CreateAsyncScope();
                        await dispatch.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(id, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception)
                    {
                        await using var retry = scopes.CreateAsyncScope();
                        await retry.ServiceProvider.GetRequiredService<NotificationDispatcher>().RetryAsync(id, token);
                        logger.LogWarning("Notification event {EventId} failed; retry scheduled.", id);
                    }
                }
                break;
            case BackgroundTask.ExpireLots:
                await services.GetRequiredService<IInventoryService>().ExpireDueLotsAsync(token); break;
            case BackgroundTask.DebtReminders:
                await services.GetRequiredService<OperationalAlertsService>().DebtRemindersAsync(o.BatchSize, o.DebtReminderDays, token); break;
            case BackgroundTask.InventoryAlerts:
                await services.GetRequiredService<OperationalAlertsService>().InventoryAlertsAsync(o.BatchSize, o.ExpiryWarningDays, token); break;
            case BackgroundTask.ExpireAuthentication:
                await services.GetRequiredService<AuthMaintenanceService>().ExpireAsync(o.BatchSize, token); break;
            case BackgroundTask.ReconcilePayments:
                var payments = await services.GetRequiredService<PaymentReconciliationService>().PendingAsync(o.BatchSize, token);
                foreach (var id in payments)
                {
                    try
                    {
                        await using (var attempt = scopes.CreateAsyncScope())
                            if (!await attempt.ServiceProvider.GetRequiredService<PaymentReconciliationService>().RecordAttemptAsync(id, token)) continue;
                        await using var payment = scopes.CreateAsyncScope();
                        await payment.ServiceProvider.GetRequiredService<PaymentReconciliationService>().ReconcileAsync(id, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception e) { logger.LogWarning("Payment {PaymentId} reconciliation failed ({ErrorType}); it will retry.", id, e.GetType().Name); }
                }
                break;
        }
    }
    private static bool Enabled(BackgroundTask task, BackgroundJobOptions o) => task switch
    {
        BackgroundTask.ExpireLots => o.ExpireLots, BackgroundTask.DebtReminders => o.DebtReminders,
        BackgroundTask.InventoryAlerts => o.InventoryAlerts, BackgroundTask.ReconcilePayments => o.ReconcilePayments,
        BackgroundTask.ExpireAuthentication => o.ExpireAuthentication, _ => true
    };
    private static int Interval(BackgroundTask task, BackgroundJobOptions o) => task switch
    {
        BackgroundTask.Notifications => o.NotificationSeconds, BackgroundTask.ExpireLots => o.ExpireLotsSeconds,
        BackgroundTask.ReconcilePayments => o.PaymentSeconds, BackgroundTask.ExpireAuthentication => o.AuthenticationSeconds,
        _ => o.AlertSeconds
    };
}
