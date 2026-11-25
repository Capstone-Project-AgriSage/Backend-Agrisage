using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

public sealed class DebtSmsDeliveryService(IAgriSageDbContext context, ISmsTextSender sms, IDateTimeProvider clock)
{
    public async Task DeliverAsync(int batchSize, CancellationToken token)
    {
        if (!sms.IsConfigured) return;
        var ids = await context.DebtSmsOutbox.AsNoTracking()
            .Where(x => x.SentAt == null && x.CancelledAt == null && x.AvailableAt <= clock.UtcNow)
            .OrderBy(x => x.AvailableAt).ThenBy(x => x.Id).Take(batchSize)
            .Select(x => x.Id).ToListAsync(token);
        foreach (var id in ids)
        {
            var item = await context.DebtSmsOutbox.SingleAsync(x => x.Id == id, token);
            if (item.SentAt is not null || item.CancelledAt is not null) continue;
            var phone = await context.DebtAccounts.AsNoTracking().Where(a => a.Id == item.DebtAccountId
                && a.FarmerProfile.User.Status == UserStatus.Active && a.FarmerProfile.User.Role.IsActive
                && a.FarmerProfile.User.Role.DeletedAt == null)
                .Select(a => a.FarmerProfile.User.PhoneNumber).SingleOrDefaultAsync(token);
            var validPhone = ContactNormalizer.TryNormalizePhone(phone, out var normalized);
            var entries = context.DebtEntries.AsNoTracking().Where(e => e.DebtAccountId == item.DebtAccountId
                && e.OutstandingAmount > 0 && e.Status != DebtEntryStatus.Disputed
                && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled);
            entries = item.CollectionCropSeasonId is { } seasonId
                ? entries.Where(e => e.CollectionCropSeasonId == seasonId)
                : entries.Where(e => e.DueDate == item.DueDate);
            var amount = validPhone ? await entries.SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0 : 0;
            if (amount <= 0)
            {
                item.Cancel(clock.UtcNow);
                await context.SaveChangesAsync(token);
                continue;
            }
            item.RefreshPhone(normalized!);
            item.RefreshMessage(DebtSmsMessage.Create(item.Milestone, amount, item.DueDate));
            try
            {
                await sms.SendAsync(item.PhoneNumber, item.Message, "DebtReminder", token);
                item.Sent(clock.UtcNow);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                item.Retry(clock.UtcNow);
            }
            await context.SaveChangesAsync(token);
        }
    }
}
