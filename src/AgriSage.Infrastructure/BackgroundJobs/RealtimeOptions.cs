using System.ComponentModel.DataAnnotations;

namespace AgriSage.Infrastructure.BackgroundJobs;

public sealed class RealtimeOptions
{
    // Used only for crash recovery/retries and lost wake-ups; never scans inventory or diagnosis tables.
    [Range(5, 300)] public int RecoverySeconds { get; init; } = 60;
    [Range(1, 100)] public int BatchSize { get; init; } = 100;
}
