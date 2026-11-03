using AgriSage.Domain.Features.Debt.Entities;

namespace AgriSage.Application.Features.Debt;

// F3.18/F3.20: the reminder milestones of a debt group, shared by the reminder job and the start of reminders so both
// agree on which milestone is "the latest one reached" and which ones come before the start date (T0).
// N2..N5 are the due date plus the policy offsets (−7, 0, +14, +30 by default); N1 is the season's harvest start.
public static class DebtReminderSchedule
{
    public static IReadOnlyList<(string Code, DateOnly Date)> Milestones(DateOnly due, DebtReminderOffsets offsets) =>
        [("N2", due.AddDays(offsets.N2)), ("N3", due), ("N4", due.AddDays(offsets.N4)), ("N5", due.AddDays(offsets.N5))];

    // The latest milestone reached by `today` that is not before `start` (null start = no lower bound). Milestones before
    // the start are never sent: the check is by date at every run, so a debt entered or given a season after the start
    // cannot bring an old milestone back.
    public static (string Code, DateOnly Date)? Latest(DateOnly due, DebtReminderOffsets offsets, DateOnly today, DateOnly? start)
    {
        (string Code, DateOnly Date)? latest = null;
        foreach (var milestone in Milestones(due, offsets))
        {
            if (milestone.Date > today) break;
            if (start is { } s && milestone.Date < s) continue;
            latest = milestone;
        }

        return latest;
    }
}
