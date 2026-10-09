using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

public sealed class PaymentReconciliationService(IAgriSageDbContext context, PayOsPaymentService payments,
    IRowLockService locks, IDateTimeProvider clock)
{
    public Task<List<Guid>> PendingAsync(int batchSize, CancellationToken token) => context.Payments.AsNoTracking()
        .Where(p => p.PaymentMethod == PaymentMethod.PayOs && p.Status == PaymentStatus.Pending && p.ProviderOrderCode != null)
        .OrderBy(p => p.LastReconciliationAttemptAt ?? p.InitiatedAt).ThenBy(p => p.Id).Take(batchSize).Select(p => p.Id).ToListAsync(token);

    // Scheduling metadata only; worker disposes this scope before provider I/O and settlement.
    public async Task<bool> RecordAttemptAsync(Guid id, CancellationToken token)
    {
        var target = await context.Payments.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.OrderId }).SingleOrDefaultAsync(token);
        if (target is null) return false;
        await using var tx = await context.BeginTransactionAsync(token);
        if (target.OrderId is { } order) await locks.LockOrderAsync(order, token);
        await locks.LockPaymentAsync(id, token);
        var payment = await context.Payments.SingleOrDefaultAsync(p => p.Id == id && p.PaymentMethod == PaymentMethod.PayOs
            && p.Status == PaymentStatus.Pending, token);
        if (payment is null) return false;
        payment.RecordReconciliationAttempt(clock.UtcNow);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return true;
    }

    public Task ReconcileAsync(Guid id, CancellationToken token) => payments.SyncSystemAsync(id, token);
}
