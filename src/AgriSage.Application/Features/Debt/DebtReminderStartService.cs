using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.20: starts debt reminders for the store, once. Until then the reminder job sends nothing. The start date T0 is today in
// the store's calendar (Vietnam time); reminder milestones before T0 are never sent (the job compares dates at every run).
// Owner only (DEBT_POLICY.MANAGE, not delegable). The dry run counts and writes nothing; the real run sets T0 and its audit in
// one transaction under the store lock, so two concurrent starts end with one T0, and running it again changes nothing.
public sealed class DebtReminderStartService(
    IAgriSageDbContext db,
    IRowLockService locks,
    CustomerWrites access,
    AuditTrail audit,
    IDateTimeProvider clock) : IDebtReminderStartService
{
    private static readonly string[] Codes = ["N1", "N2", "N3", "N4", "N5"];

    public async Task<DebtReminderStartResponse> StartAsync(DebtReminderStartRequest request, CancellationToken token)
    {
        await access.OwnerActorAsync("DEBT_POLICY.MANAGE", token);
        var store = await ActiveStore.GetIdAsync(db, token);
        var today = BusinessCalendar.Today(clock.UtcNow);

        if (request.DryRun)
        {
            var existing = await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token);
            var offsets = (existing ?? new StoreDebtPolicy(store)).ReminderOffsets;
            var start = existing?.RemindersStartDate ?? today;
            return await CountAsync(store, offsets, today, start, dryRun: true, alreadyStarted: existing?.RemindersStartDate is not null, token);
        }

        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var policy = await db.StoreDebtPolicies.SingleOrDefaultAsync(p => p.StoreId == store, token);
        if (policy?.RemindersStartDate is { } started)
        {
            var again = await CountAsync(store, policy.ReminderOffsets, today, started, dryRun: false, alreadyStarted: true, token);
            await tx.CommitAsync(token);
            return again;
        }

        var created = policy is null;
        if (policy is null)
        {
            policy = new StoreDebtPolicy(store);
            db.StoreDebtPolicies.Add(policy);
        }

        policy.StartReminders(today);
        var response = await CountAsync(store, policy.ReminderOffsets, today, today, dryRun: false, alreadyStarted: false, token);
        audit.Record("DEBT_REMINDERS_STARTED", "STORE_DEBT_POLICY", policy.Id, store,
            newValues: new { StartDate = today, response.Skipped, response.SkippedTotal, response.ActiveGroups, PolicyCreated = created },
            reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return response;
    }

    // Counts what the job would have sent as a backlog before T0 (the latest milestone reached, if it is before T0), the same
    // way the job picks it. Disputed, paid and cancelled entries are not reminded, so they are not counted.
    private async Task<DebtReminderStartResponse> CountAsync(Guid store, DebtReminderOffsets offsets, DateOnly today,
        DateOnly start, bool dryRun, bool alreadyStarted, CancellationToken token)
    {
        var open = from e in db.DebtEntries.AsNoTracking()
                   join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                   where a.StoreId == store && e.OutstandingAmount > 0 && e.Status != DebtEntryStatus.Disputed
                       && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled
                   select new { a.FarmerProfileId, e.DueDate, e.CollectionCropSeasonId };
        var dueGroups = await open.GroupBy(x => new { x.FarmerProfileId, x.DueDate })
            .Select(g => new { g.Key.FarmerProfileId, g.Key.DueDate }).ToListAsync(token);
        var seasonGroups = await open.Where(x => x.CollectionCropSeasonId != null)
            .GroupBy(x => new { x.FarmerProfileId, x.CollectionCropSeasonId })
            .Select(g => new { g.Key.FarmerProfileId, SeasonId = g.Key.CollectionCropSeasonId!.Value }).ToListAsync(token);
        // The seasons the job sends N1 for: open, harvest started, N2 of their due date not yet come.
        var seasons = (await db.CropSeasons.AsNoTracking()
                .Where(s => s.StoreId == store && s.ClosedAt == null && s.HarvestStart <= today).ToListAsync(token))
            .Where(s => today < s.PaymentDueDate.AddDays(offsets.N2)).ToDictionary(s => s.Id);

        var skipped = Codes.ToDictionary(c => c, _ => 0);
        var upcoming = 0;
        foreach (var group in dueGroups)
        {
            if (DebtReminderSchedule.Latest(group.DueDate, offsets, today, null) is { } latest && latest.Date < start)
                skipped[latest.Code]++;
            if (DebtReminderSchedule.Milestones(group.DueDate, offsets).Any(m => m.Date >= start)) upcoming++;
        }

        foreach (var group in seasonGroups)
            if (seasons.TryGetValue(group.SeasonId, out var season) && season.HarvestStart < start)
                skipped["N1"]++;

        var farmers = dueGroups.Select(g => g.FarmerProfileId).Distinct().Count();
        return new DebtReminderStartResponse(dryRun, alreadyStarted, start, farmers, dueGroups.Count, skipped, skipped.Values.Sum(), upcoming);
    }
}
