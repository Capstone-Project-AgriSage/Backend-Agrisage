using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Debt.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.12: the store debt policy. Reading is open to Sale staff (DEBT_POLICY.READ); changing it is owner-only
// (DEBT_POLICY.MANAGE is not delegable and the role is checked again here, as in F3.21). A store without a saved policy
// reads the documented defaults; the first save creates its row.
public sealed class DebtPolicyService(
    IAgriSageDbContext db,
    IRowLockService locks,
    CustomerWrites access,
    AuditTrail audit) : IDebtPolicyService
{
    public async Task<DebtPolicyResponse> GetAsync(CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var policy = await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token);
        return Respond(policy ?? new StoreDebtPolicy(store), isDefault: policy is null);
    }

    // Changing the default grace only proposes the grace of new seasons; seasons already set keep theirs (CS-05).
    public async Task<DebtPolicyResponse> UpdateAsync(UpdateDebtPolicyRequest request, CancellationToken token)
    {
        await access.OwnerActorAsync("DEBT_POLICY.MANAGE", token);
        var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var policy = await db.StoreDebtPolicies.SingleOrDefaultAsync(p => p.StoreId == store, token);
        var created = policy is null;
        if (policy is null)
        {
            // The default was read with version 0. The row is first stored with the defaults (version 0) and then
            // changed (version 1), inside this one transaction, so an editor still holding the default cannot overwrite it.
            if (request.Version != 0) throw new ConflictException("The debt policy changed. Reload it before saving.");
            policy = new StoreDebtPolicy(store);
            db.StoreDebtPolicies.Add(policy);
            await db.SaveChangesAsync(token);
        }
        else if (policy.Version != request.Version)
        {
            throw new ConflictException("The debt policy changed. Reload it before saving.");
        }

        var before = Snapshot.Of(policy);
        policy.Update(request.DefaultGraceDays,
            new DebtReminderOffsets(request.ReminderOffsets.N2, request.ReminderOffsets.N4, request.ReminderOffsets.N5),
            request.StaffRolloverLimit, request.OwnerReviewAfterRollovers, request.OnTimeSeasonsForLimitRaise,
            request.MaxCreditTermDays, request.MaxTotalExtensionDays);
        var after = Snapshot.Of(policy);
        if (created || before != after)
        {
            audit.Record("DEBT_POLICY_CHANGED", "STORE_DEBT_POLICY", policy.Id, store, before, after,
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
            await db.SaveChangesAsync(token);
        }

        await tx.CommitAsync(token);
        return Respond(policy, isDefault: false);
    }

    private static DebtPolicyResponse Respond(StoreDebtPolicy p, bool isDefault) => new(p.DefaultGraceDays,
        new(p.ReminderOffsets.N2, p.ReminderOffsets.N4, p.ReminderOffsets.N5), p.StaffRolloverLimit,
        p.OwnerReviewAfterRollovers, p.OnTimeSeasonsForLimitRaise, p.MaxCreditTermDays, p.MaxTotalExtensionDays, p.Version,
        isDefault);

    // Record equality tells whether the save changed anything; the audit stores it as old and new values.
    private sealed record Snapshot(
        int DefaultGraceDays,
        DebtReminderOffsets ReminderOffsets,
        int StaffRolloverLimit,
        int OwnerReviewAfterRollovers,
        int OnTimeSeasonsForLimitRaise,
        int? MaxCreditTermDays,
        int MaxTotalExtensionDays)
    {
        public static Snapshot Of(StoreDebtPolicy p) => new(p.DefaultGraceDays, p.ReminderOffsets, p.StaffRolloverLimit,
            p.OwnerReviewAfterRollovers, p.OnTimeSeasonsForLimitRaise, p.MaxCreditTermDays, p.MaxTotalExtensionDays);
    }
}
