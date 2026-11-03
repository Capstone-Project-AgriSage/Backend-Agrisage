using System.Linq.Expressions;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.11: the store's debt-collection seasons. Writes are owner-only (CROP_SEASONS.MANAGE is not delegable, and the role is
// checked again here as in F3.21) and serialized per store, so seasons keep chaining without gap or overlap (D3).
public sealed class CropSeasonService(
    IAgriSageDbContext db,
    IRowLockService locks,
    IDateTimeProvider clock,
    ICurrentUserService currentUser,
    CustomerWrites access,
    AuditTrail audit) : ICropSeasonService
{
    private DateOnly Today => BusinessCalendar.Today(clock.UtcNow);

    public async Task<PagedResult<CropSeasonResponse>> ListAsync(CropSeasonListRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var query = db.CropSeasons.AsNoTracking().Where(s => s.StoreId == store);
        if (request.Year is { } year) query = query.Where(s => s.HarvestStart.Year == year);
        if (request.Status is { } status) query = query.Where(InStatus(status, Today));
        var total = await query.LongCountAsync(token);
        var page = await query.OrderByDescending(s => s.HarvestStart).ThenBy(s => s.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return new(await RespondAsync(page, token), request.Page, request.PageSize, total);
    }

    public async Task<CropSeasonResponse> GetAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var season = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.StoreId == store, token)
            ?? throw new NotFoundException("Crop season", id);
        return (await RespondAsync([season], token)).Single();
    }

    public async Task<CropSeasonResponse> CreateAsync(CreateCropSeasonRequest request, CancellationToken token)
    {
        await access.OwnerActorAsync("CROP_SEASONS.MANAGE", token);
        var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var seasons = await db.CropSeasons.Where(s => s.StoreId == store).ToListAsync(token);
        var code = request.Code.Trim();
        if (seasons.Any(s => s.Code == code)) throw new ConflictException($"Season code '{code}' already exists in this store.");

        // D3: a new season is only added after the last one; its order window opens on the last harvest start.
        var windowStart = SeasonCalendar.NextOrderWindowStart(seasons);
        if (windowStart is null)
        {
            windowStart = request.OrderWindowStart
                ?? throw new BusinessRuleException("The first season needs the date its order window opens (orderWindowStart).");
        }
        else if (request.OrderWindowStart is { } given && given != windowStart)
        {
            throw new BusinessRuleException(
                $"A new season opens its order window on {windowStart:yyyy-MM-dd}, the harvest start of the last season (no gap, no overlap).");
        }

        if (request.HarvestStart <= windowStart)
        {
            throw new BusinessRuleException("A new season is added after the last one: its harvest must start after its order window opens.");
        }

        await RequireSeasonalRuleAsync(store, request.SeasonalRuleId, token);
        var grace = request.GraceDays ?? await db.StoreDebtPolicies.AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => (int?)p.DefaultGraceDays).SingleOrDefaultAsync(token) ?? 30;
        var season = new CropSeason(store, code, request.Name, string.IsNullOrWhiteSpace(request.CropType) ? "RICE" : request.CropType,
            windowStart.Value, request.HarvestStart, request.HarvestEnd, grace, request.SeasonalRuleId);
        SeasonCalendar.ValidateSequence(seasons.Append(season));
        db.CropSeasons.Add(season);
        audit.Record("CROP_SEASON_CREATED", "CROP_SEASON", season.Id, store, newValues: Snapshot(season));
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await GetAsync(season.Id, token);
    }

    public async Task<CropSeasonResponse> UpdateAsync(Guid id, UpdateCropSeasonRequest request, CancellationToken token)
    {
        var actor = await access.OwnerActorAsync("CROP_SEASONS.MANAGE", token);
        var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var seasons = await db.CropSeasons.Where(s => s.StoreId == store).OrderBy(s => s.HarvestStart).ToListAsync(token);
        var season = seasons.SingleOrDefault(s => s.Id == id) ?? throw new NotFoundException("Crop season", id);
        if (season.Version != request.Version) throw new ConflictException("The crop season changed. Reload it before saving.");
        if (season.IsClosed) throw new BusinessRuleException($"Season '{season.Code}' is closed and can no longer change.");

        var before = Snapshot(season);
        var oldDue = season.PaymentDueDate;
        var next = seasons.SkipWhile(s => s.Id != id).Skip(1).FirstOrDefault();
        if (next is not null && request.HarvestStart >= next.HarvestStart)
        {
            throw new BusinessRuleException($"The harvest must start before the next season '{next.Code}' starts its harvest.");
        }

        await RequireSeasonalRuleAsync(store, request.SeasonalRuleId, token);
        season.Update(request.Name, request.HarvestStart, request.HarvestEnd, request.GraceDays);
        season.LinkSeasonalRule(request.SeasonalRuleId);
        // The next season's order window starts on this harvest start: move it in the same transaction (D3).
        if (next is not null && next.OrderWindowStart != season.HarvestStart) next.MoveOrderWindowStart(season.HarvestStart);
        SeasonCalendar.ValidateSequence(seasons);

        var moved = 0;
        if (request.ApplyNewDueDateToCollectingDebts && season.PaymentDueDate != oldDue)
        {
            if (season.PaymentDueDate < oldDue)
            {
                throw new BusinessRuleException("Recorded debt can only be given a later due date, never an earlier one.");
            }

            moved = await ExtendCollectingDebtAsync(season, oldDue, request.Reason!.Trim(), actor, token);
        }

        audit.Record("CROP_SEASON_UPDATED", "CROP_SEASON", season.Id, store, before,
            new { season = Snapshot(season), debtsMovedToNewDueDate = moved }, request.Reason?.Trim());
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await GetAsync(season.Id, token);
    }

    public async Task<MyCurrentCropSeasonResponse> MyCurrentAsync(CancellationToken token)
    {
        var user = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var store = await ActiveStore.GetIdAsync(db, token);
        var farmer = await db.FarmerProfiles.AsNoTracking().Where(f => f.UserId == user && f.User.Role.Code == RoleCode.Farmer)
            .Select(f => (Guid?)f.Id).SingleOrDefaultAsync(token) ?? throw new ForbiddenException();
        var today = Today;
        var season = SeasonCalendar.Resolve(await db.CropSeasons.AsNoTracking()
            .Where(s => s.StoreId == store && s.ClosedAt == null && s.OrderWindowStart <= today && today < s.HarvestStart)
            .ToListAsync(token), today);
        var cap = await db.StoreDebtPolicies.AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => p.MaxCreditTermDays).SingleOrDefaultAsync(token);
        if (season is not null)
        {
            var due = SeasonCalendar.DueDate(season.PaymentDueDate, today, cap);
            return new(season.Id, season.Code, season.Name, season.HarvestStart, season.HarvestEnd, due,
                due < season.PaymentDueDate ? "CAPPED" : "SEASON", null, true);
        }

        var term = await (from p in db.FarmerCreditProfiles.AsNoTracking()
                          join t in db.CreditTiers.AsNoTracking() on p.CreditTierId equals t.Id
                          where p.StoreId == store && p.FarmerProfileId == farmer
                          select (int?)t.DefaultPaymentTermDays).SingleOrDefaultAsync(token);
        return term is { } days
            ? new(null, null, null, null, null, today.AddDays(days), "TERM_DAYS", days, true)
            : new(null, null, null, null, null, null, "NONE", null, true);
    }

    // Entries collected in this season that still follow its old due date move to the new one; entries with their own
    // due date (a term cap, an earlier change or rollover to another date) keep it.
    private async Task<int> ExtendCollectingDebtAsync(CropSeason season, DateOnly oldDue, string reason, Guid actor, CancellationToken token)
    {
        var entries = await db.DebtEntries.Include(e => e.Actions)
            .Where(e => e.CollectionCropSeasonId == season.Id && e.DueDate == oldDue && e.OutstandingAmount > 0
                && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled)
            .ToListAsync(token);
        foreach (var entry in entries)
        {
            var action = entry.ChangeDueDate(season.PaymentDueDate, reason, actor);
            audit.Record("DEBT_CHANGE_DUE_DATE", "DEBT_ENTRY", entry.Id, season.StoreId,
                new { dueDate = oldDue }, new { dueDate = season.PaymentDueDate, cropSeasonId = season.Id, actionId = action.Id }, reason);
        }

        return entries.Count;
    }

    private async Task RequireSeasonalRuleAsync(Guid store, Guid? seasonalRuleId, CancellationToken token)
    {
        if (seasonalRuleId is { } rule && !await db.SeasonalRules.AnyAsync(r => r.Id == rule && r.StoreId == store, token))
        {
            throw new BusinessRuleException("The linked seasonal rule does not belong to this store.");
        }
    }

    private async Task<List<CropSeasonResponse>> RespondAsync(IReadOnlyList<CropSeason> seasons, CancellationToken token)
    {
        var ids = seasons.Select(s => s.Id).ToList();
        var open = db.DebtEntries.AsNoTracking().Where(e => e.OutstandingAmount > 0
            && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled);
        var originated = await open.Where(e => e.OriginCropSeasonId != null && ids.Contains(e.OriginCropSeasonId.Value))
            .GroupBy(e => e.OriginCropSeasonId!.Value)
            .Select(g => new { Id = g.Key, Amount = g.Sum(e => e.OutstandingAmount), Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, token);
        var collecting = await open.Where(e => e.CollectionCropSeasonId != null && ids.Contains(e.CollectionCropSeasonId.Value))
            .GroupBy(e => e.CollectionCropSeasonId!.Value)
            .Select(g => new { Id = g.Key, Amount = g.Sum(e => e.OutstandingAmount), Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, token);
        var today = Today;
        return seasons.Select(s => new CropSeasonResponse(s.Id, s.Code, s.Name, s.CropType, s.OrderWindowStart, s.HarvestStart,
            s.HarvestEnd, s.GraceDays, s.PaymentDueDate, EnumText.Format(s.PhaseOn(today)), s.SeasonalRuleId, s.ClosedAt, s.Version,
            originated.TryGetValue(s.Id, out var o) ? new(o.Amount, o.Count) : new(0, 0),
            collecting.TryGetValue(s.Id, out var c) ? new(c.Amount, c.Count) : new(0, 0))).ToList();
    }

    // The phase filter in SQL, mirroring CropSeason.PhaseOn.
    private static Expression<Func<CropSeason, bool>> InStatus(string status, DateOnly today) => status switch
    {
        "CLOSED" => s => s.ClosedAt != null,
        "PLANNED" => s => s.ClosedAt == null && today < s.OrderWindowStart,
        "OPEN" => s => s.ClosedAt == null && s.OrderWindowStart <= today && today < s.HarvestStart,
        "HARVESTING" => s => s.ClosedAt == null && s.HarvestStart <= today && today <= s.HarvestEnd,
        "COLLECTING" => s => s.ClosedAt == null && s.HarvestEnd < today,
        _ => throw new DomainException("Unknown season status."),
    };

    private static object Snapshot(CropSeason s) => new
    {
        s.Code, s.Name, s.CropType, s.OrderWindowStart, s.HarvestStart, s.HarvestEnd, s.GraceDays, s.PaymentDueDate, s.SeasonalRuleId
    };
}
