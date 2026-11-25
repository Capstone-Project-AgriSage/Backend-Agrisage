namespace AgriSage.Application.Common.Interfaces;

// New tasks go at the end: the advisory lock key of a task is its position.
public enum BackgroundTask { Notifications, ExpireLots, DebtReminders, InventoryAlerts, ReconcilePayments, ExpireAuthentication, DebtSms, SeasonalSuggestions }

public interface IBackgroundJobLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(BackgroundTask task, CancellationToken token);
}
