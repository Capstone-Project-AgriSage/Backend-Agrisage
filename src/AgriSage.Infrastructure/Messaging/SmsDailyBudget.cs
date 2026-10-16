using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Infrastructure.Messaging;

// In-memory cap on real SMS per Vietnam calendar day (UTC+7), so a retry loop or a stray test cannot spend credit
// unnoticed. It is a safety net for one API process, not an accounting ledger: a restart starts the count again.
public sealed class SmsDailyBudget(IDateTimeProvider clock)
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private readonly object gate = new();
    private DateOnly day;
    private int used;

    // limit <= 0 means unlimited. Returns false (and counts nothing) once the day's allowance is spent.
    public bool TryTake(int limit)
    {
        if (limit <= 0) return true;
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(Vietnam).DateTime);
        lock (gate)
        {
            if (today != day) { day = today; used = 0; }
            if (used >= limit) return false;
            used++;
            return true;
        }
    }
}
