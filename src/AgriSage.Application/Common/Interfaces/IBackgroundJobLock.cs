namespace AgriSage.Application.Common.Interfaces;

public enum BackgroundTask { Notifications, ExpireLots, DebtReminders, InventoryAlerts, ReconcilePayments, ExpireAuthentication }

public interface IBackgroundJobLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(BackgroundTask task, CancellationToken token);
}
