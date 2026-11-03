using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20: the reminder job sends nothing until the store has a start date (T0). Tests of the milestones themselves start the
// reminders long ago (everything counts, as before F3.20); tests of T0 set their own date first, and a start date already set
// is never changed (as in the product), so the call order decides.
internal static class ReminderStartHelper
{
    public static readonly DateOnly LongAgo = new(2000, 1, 1);

    public static async Task EnsureAsync(AgriSageDbContext db, Guid storeId, CancellationToken token, DateOnly? start = null)
    {
        var policy = await db.StoreDebtPolicies.SingleOrDefaultAsync(p => p.StoreId == storeId, token);
        if (policy is null)
        {
            policy = new StoreDebtPolicy(storeId);
            db.StoreDebtPolicies.Add(policy);
        }

        if (policy.StartReminders(start ?? LongAgo) || db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(token);
    }
}
