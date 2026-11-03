using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.14: extend one debt entry to a later season. Only the collection season and the due date move (D9); the amount, the
// ledger, the origin season and the original due date stay. Serialized per Farmer like every other debt change.
public sealed class DebtRolloverService(
    IAgriSageDbContext db,
    IRowLockService locks,
    ICurrentUserService currentUser,
    CustomerWrites access,
    AuditTrail audit,
    IDebtService debts) : IDebtRolloverService
{
    public async Task<DebtEntryResponse> RolloverAsync(Guid entryId, DebtRolloverRequest request, CancellationToken token)
    {
        var actor = await access.ActorAsync("DEBT.ROLLOVER", token);
        var owner = RoleCodeFormat.TryParse(currentUser.Role, out var role) && role is RoleCode.Admin or RoleCode.StoreOwner;
        if (!EnumText.TryParse<DebtReasonCode>(request.ReasonCode, out var code) || !DebtReasonCodes.AllowsRollover(code))
        {
            throw new BusinessRuleException(DebtRolloverRequestValidator.NotForRollover);
        }

        if (string.IsNullOrWhiteSpace(request.Note)) throw new BusinessRuleException("A rollover needs a note.");

        var store = await ActiveStore.GetIdAsync(db, token);
        var farmer = await (from e in db.DebtEntries.AsNoTracking()
                            join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                            where e.Id == entryId && a.StoreId == store
                            select (Guid?)a.FarmerProfileId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Debt entry", entryId);

        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockFarmerProfileAsync(farmer, token);
        var entry = await db.DebtEntries.Include(e => e.Actions).SingleAsync(e => e.Id == entryId, token);
        var policy = await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token)
            ?? new StoreDebtPolicy(store);

        // CS-08: Sale staff extend while the entry has fewer rollovers than the store allows; then the owner decides.
        if (!owner && !policy.StaffMayRollOver(entry.RolloverCount))
        {
            throw new ForbiddenException(
                $"Debt entry '{entry.EntryNumber}' was already extended {entry.RolloverCount} time(s); the store owner decides further extensions.");
        }

        var target = await TargetAsync(store, entry, request.TargetCropSeasonId, token);

        // D5: the total extension from the original due date. Staff stay within it; the owner may go past it, but only by
        // confirming it.
        var exceeds = policy.ExceedsTotalExtension(entry.OriginalDueDate, target.PaymentDueDate);
        if (exceeds && (!owner || !request.OverrideExtensionLimit))
        {
            throw new BusinessRuleException(owner
                ? $"The new due date is more than {policy.MaxTotalExtensionDays} days after the original due date; confirm the override to go past the limit."
                : $"The new due date is more than {policy.MaxTotalExtensionDays} days after the original due date; only the store owner can extend further.");
        }

        var before = new { entry.DueDate, entry.CollectionCropSeasonId, entry.RolloverCount };
        var action = entry.Rollover(target.Id, target.PaymentDueDate, code, request.Note.Trim(), actor);
        db.DebtEntryActions.Add(action);
        audit.Record("DEBT_ROLLOVER", "DEBT_ENTRY", entry.Id, store, before, new
        {
            entry.DueDate, entry.CollectionCropSeasonId, entry.RolloverCount, entry.OriginalDueDate,
            ReasonCode = EnumText.Format(code), OverrideExtensionLimit = exceeds, ActionId = action.Id,
        }, request.Note.Trim());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await debts.EntryAsync(entry.Id, token);
    }

    // The given season, or the next open season whose due date is later than the entry's (seasons chain in time, D3).
    private async Task<CropSeason> TargetAsync(Guid store, DebtEntry entry, Guid? targetId, CancellationToken token)
    {
        if (targetId is { } id)
        {
            var season = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.StoreId == store, token)
                ?? throw new BusinessRuleException("The target season does not exist in this store.");
            if (season.IsClosed) throw new BusinessRuleException($"Season '{season.Code}' is closed; debt cannot move into it.");
            return season;
        }

        var due = entry.DueDate;
        var current = entry.CollectionCropSeasonId;
        return await db.CropSeasons.AsNoTracking()
                   .Where(s => s.StoreId == store && s.ClosedAt == null && s.PaymentDueDate > due && s.Id != current)
                   .OrderBy(s => s.HarvestStart).FirstOrDefaultAsync(token)
               ?? throw new BusinessRuleException("No open season has a later due date; add the next season first.");
    }
}
