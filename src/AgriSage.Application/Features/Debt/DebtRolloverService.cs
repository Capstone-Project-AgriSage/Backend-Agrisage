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
        // Store first, then the Farmer, as the credit and customer services lock. The store lock (F3.19) serializes this
        // with closing a season; the target is read after it, so debt never moves into a season that has just been closed.
        await locks.LockStoreAsync(store, token);
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

    // F3.15: the same rules entry by entry; what qualifies moves in one transaction, the rest is reported with a reason.
    public async Task<CropSeasonRolloverResponse> RolloverSeasonAsync(Guid seasonId, CropSeasonRolloverRequest request,
        CancellationToken token)
    {
        var actor = await access.ActorAsync("DEBT.ROLLOVER", token);
        var owner = RoleCodeFormat.TryParse(currentUser.Role, out var role) && role is RoleCode.Admin or RoleCode.StoreOwner;
        if (!EnumText.TryParse<DebtReasonCode>(request.ReasonCode, out var code) || !DebtReasonCodes.AllowsRollover(code))
        {
            throw new BusinessRuleException(DebtRolloverRequestValidator.NotForRollover);
        }

        if (string.IsNullOrWhiteSpace(request.Note)) throw new BusinessRuleException("A rollover needs a note.");
        var note = request.Note.Trim();
        var store = await ActiveStore.GetIdAsync(db, token);
        var source = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seasonId && s.StoreId == store, token)
            ?? throw new NotFoundException("Crop season", seasonId);
        var target = await SeasonTargetAsync(store, source, request.TargetCropSeasonId, token);

        // Candidates: the listed entries of this store, or every open entry collected in the source season.
        var requested = request.EntryIds?.Distinct().ToList();
        var candidates = from e in db.DebtEntries.AsNoTracking()
                         join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                         where a.StoreId == store
                         select new { e.Id, a.FarmerProfileId, e.CollectionCropSeasonId, e.OutstandingAmount, e.Status };
        candidates = requested is null
            ? candidates.Where(e => e.CollectionCropSeasonId == source.Id && e.OutstandingAmount > 0
                && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled)
            : candidates.Where(e => requested.Contains(e.Id));
        var found = await candidates.Select(e => new { e.Id, e.FarmerProfileId }).ToListAsync(token);
        if (found.Count > CropSeasonRolloverRequestValidator.MaxEntries)
        {
            throw new BusinessRuleException(
                $"The season has {found.Count} open entries; roll over at most {CropSeasonRolloverRequestValidator.MaxEntries} per call by listing entryIds.");
        }

        // Writes lock every Farmer in farmerProfileId order (as payments lock one Farmer), so concurrent debt changes never
        // deadlock. A dry run reads only.
        await using var tx = request.DryRun ? null : await db.BeginTransactionAsync(token);
        if (!request.DryRun)
        {
            // Store first (as closing a season and the credit services lock), then each Farmer in order.
            await locks.LockStoreAsync(store, token);
            foreach (var farmer in found.Select(e => e.FarmerProfileId).Distinct().Order()) await locks.LockFarmerProfileAsync(farmer, token);
            // F3.19: the target must still be open once the store is locked.
            if (await db.CropSeasons.AsNoTracking().AnyAsync(s => s.Id == target.Id && s.ClosedAt != null, token))
            {
                throw new BusinessRuleException($"Season '{target.Code}' is closed; debt cannot move into it.");
            }
        }

        var ids = found.Select(e => e.Id).ToList();
        var entries = request.DryRun
            ? await db.DebtEntries.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(token)
            : await db.DebtEntries.Where(e => ids.Contains(e.Id)).ToListAsync(token);
        var farmers = found.ToDictionary(e => e.Id, e => e.FarmerProfileId);
        var policy = await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token)
            ?? new StoreDebtPolicy(store);

        var rolled = new List<CropSeasonRolledOverEntry>();
        var skipped = (requested ?? []).Where(id => !farmers.ContainsKey(id))
            .Select(id => new CropSeasonSkippedEntry(id, null, "NOT_FOUND")).ToList();
        foreach (var entry in entries.OrderBy(e => e.EntryNumber))
        {
            var exceeds = policy.ExceedsTotalExtension(entry.OriginalDueDate, target.PaymentDueDate);
            var reason = entry.CollectionCropSeasonId != source.Id ? "NOT_IN_SEASON"
                : entry.OutstandingAmount == 0 || entry.Status is DebtEntryStatus.Paid or DebtEntryStatus.Cancelled ? "NOTHING_OWED"
                : entry.Status == DebtEntryStatus.Disputed ? "DISPUTED"
                : target.PaymentDueDate <= entry.DueDate ? "NOT_LATER"
                : !owner && !policy.StaffMayRollOver(entry.RolloverCount) ? "NEEDS_OWNER"
                : exceeds && (!owner || !request.OverrideExtensionLimit) ? "EXCEEDS_TOTAL_EXTENSION"
                : null;
            if (reason is not null)
            {
                skipped.Add(new(entry.Id, entry.EntryNumber, reason));
                continue;
            }

            rolled.Add(new(entry.Id, entry.EntryNumber, farmers[entry.Id], entry.DueDate, target.PaymentDueDate, entry.OutstandingAmount));
            if (request.DryRun) continue;
            var before = new { entry.DueDate, entry.CollectionCropSeasonId, entry.RolloverCount };
            var action = entry.Rollover(target.Id, target.PaymentDueDate, code, note, actor);
            db.DebtEntryActions.Add(action);
            audit.Record("DEBT_ROLLOVER", "DEBT_ENTRY", entry.Id, store, before, new
            {
                entry.DueDate, entry.CollectionCropSeasonId, entry.RolloverCount, entry.OriginalDueDate,
                ReasonCode = EnumText.Format(code), OverrideExtensionLimit = exceeds, ActionId = action.Id, SeasonRollover = source.Id,
            }, note);
        }

        if (tx is not null && rolled.Count > 0)
        {
            audit.Record("CROP_SEASON_DEBT_ROLLOVER", "CROP_SEASON", source.Id, store, null, new
            {
                TargetCropSeasonId = target.Id, NewDueDate = target.PaymentDueDate, ReasonCode = EnumText.Format(code),
                RolledOver = rolled.Count, Skipped = skipped.Count, EntryIds = rolled.Select(r => r.EntryId),
            }, note);
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }

        return new(source.Id, new(target.Id, target.Code, target.Name), target.PaymentDueDate, request.DryRun, rolled,
            skipped.OrderBy(s => s.EntryNumber is null).ThenBy(s => s.EntryNumber).ToList());
    }

    // The given season (open, later than the source), or the next open season whose due date is later than the source's.
    private async Task<CropSeason> SeasonTargetAsync(Guid store, CropSeason source, Guid? targetId, CancellationToken token)
    {
        if (targetId is { } id)
        {
            var season = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.StoreId == store, token)
                ?? throw new BusinessRuleException("The target season does not exist in this store.");
            if (season.IsClosed) throw new BusinessRuleException($"Season '{season.Code}' is closed; debt cannot move into it.");
            if (season.Id == source.Id || season.PaymentDueDate <= source.PaymentDueDate)
            {
                throw new BusinessRuleException("Debt can only move to a later season.");
            }

            return season;
        }

        var due = source.PaymentDueDate;
        return await db.CropSeasons.AsNoTracking()
                   .Where(s => s.StoreId == store && s.ClosedAt == null && s.PaymentDueDate > due && s.Id != source.Id)
                   .OrderBy(s => s.HarvestStart).FirstOrDefaultAsync(token)
               ?? throw new BusinessRuleException("No open season has a later due date; add the next season first.");
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
