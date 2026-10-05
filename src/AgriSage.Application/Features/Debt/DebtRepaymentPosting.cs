using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

public sealed class DebtRepaymentPosting(IAgriSageDbContext db, IRowLockService locks,
    IDateTimeProvider clock) : IDebtRepaymentPosting
{
    public async Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(Payment payment,
        IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId, CancellationToken token)
    {
        if (payment.Status != PaymentStatus.Paid || payment.PaymentContext != PaymentContext.DebtRepayment
            || payment.PayerFarmerProfileId is not { } farmer)
            throw new BusinessRuleException("A paid debt repayment must name its customer.");
        await locks.LockFarmerProfileAsync(farmer, token);
        // Caller locks Payment before loading its allocations; a fully allocated retry has no new work.
        if (payment.UnallocatedAmount == 0) return payment.Allocations.Where(a => a.IsActive && a.DebtEntryId != null)
            .Select(a => new DebtAllocationResult(a.DebtEntryId!.Value, a.Id, a.AllocatedAmount)).ToList();
        var account = await db.DebtAccounts.SingleOrDefaultAsync(a => a.StoreId == payment.StoreId && a.FarmerProfileId == farmer, token)
            ?? throw new BusinessRuleException("The customer has no debt account in this store.");
        var entries = await db.DebtEntries.Where(e => e.DebtAccountId == account.Id && e.OutstandingAmount > 0
            && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled)
            .OrderBy(e => e.DueDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(token);
        var allocations = requested?.ToList() ?? [];
        if (requested == null)
        {
            var left = payment.UnallocatedAmount;
            foreach (var entry in entries)
            {
                if (left == 0) break;
                var amount = Math.Min(left, entry.OutstandingAmount);
                allocations.Add(new(entry.Id, amount));
                left -= amount;
            }
        }
        if (allocations.Count == 0 || allocations.Select(a => a.DebtEntryId).Distinct().Count() != allocations.Count
            || allocations.Sum(a => a.Amount) != payment.UnallocatedAmount)
            throw new BusinessRuleException("Allocations must uniquely target open debts and cover the entire payment.");
        // Validate every target before mutating any entry.
        foreach (var allocation in allocations)
        {
            var entry = entries.SingleOrDefault(e => e.Id == allocation.DebtEntryId);
            if (entry == null || allocation.Amount <= 0 || allocation.Amount > entry.OutstandingAmount)
                throw new BusinessRuleException("Debt allocation exceeds the customer's current outstanding debt.");
        }
        var result = new List<DebtAllocationResult>();
        foreach (var a in allocations)
        {
            var entry = entries.Single(e => e.Id == a.DebtEntryId);
            var allocation = payment.AllocateToDebtEntry(entry.Id, a.Amount, clock.UtcNow, actorId);
            db.PaymentAllocations.Add(allocation);
            db.DebtTransactions.Add(account.ApplyPayment(entry, allocation, clock.UtcNow, actorId));
            result.Add(new(entry.Id, allocation.Id, a.Amount));
        }
        return result;
    }
}
