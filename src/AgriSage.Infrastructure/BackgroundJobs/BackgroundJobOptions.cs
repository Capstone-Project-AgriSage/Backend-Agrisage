using System.ComponentModel.DataAnnotations;

namespace AgriSage.Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public bool Enabled { get; init; }
    [Range(1, 300)] public int PollSeconds { get; init; } = 15;
    [Range(1, 100)] public int BatchSize { get; init; } = 100;
    [Range(10, 600)] public int JobTimeoutSeconds { get; init; } = 300;
    [Range(1, 3600)] public int NotificationSeconds { get; init; } = 15;
    [Range(10, 86400)] public int ExpireLotsSeconds { get; init; } = 600;
    [Range(10, 86400)] public int AlertSeconds { get; init; } = 3600;
    [Range(10, 86400)] public int PaymentSeconds { get; init; } = 120;
    [Range(10, 86400)] public int AuthenticationSeconds { get; init; } = 3600;
    [Range(1, 365)] public int ExpiryWarningDays { get; init; } = 30;
    // Debt-reminder summaries written per run when catching up after a failed run (one per day and milestone N3/N4/N5).
    [Range(1, 1000)] public int DebtSummaryBatch { get; init; } = 30;
    public bool ExpireLots { get; init; } = true;
    public bool DebtReminders { get; init; } = true;
    public bool InventoryAlerts { get; init; } = true;
    public bool ReconcilePayments { get; init; } = true;
    public bool ExpireAuthentication { get; init; } = true;
}
