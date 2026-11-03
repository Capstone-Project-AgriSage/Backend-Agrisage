using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.19: closing a season (D11), the debt-by-season report (D9: origin vs collection season) and credit-limit suggestions
// (D12). Closing is owner-only and serialized with every season change and every rollover by the store lock.
public sealed class CropSeasonClosingService(
    IAgriSageDbContext db,
    IRowLockService locks,
    IDateTimeProvider clock,
    CustomerWrites access,
    AuditTrail audit,
    ICropSeasonService seasons) : ICropSeasonClosingService
{
    private const int ListLimit = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly OrderStatus[] OpenOrderStatuses =
        [OrderStatus.PendingConfirmation, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.ReadyForFulfillment, OrderStatus.PartiallyFulfilled];

    private DateOnly Today => BusinessCalendar.Today(clock.UtcNow);

    // What closed_summary keeps: the figures as they stood when the season closed.
    private sealed record ClosedSummary(SeasonOriginFigures Origin, SeasonCollectionFigures Collection, DateOnly ClosedOn);

    public async Task<CropSeasonClosingCheckResponse> ClosingCheckAsync(Guid seasonId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var season = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(s => s.Id == seasonId && s.StoreId == store, token)
            ?? throw new NotFoundException("Crop season", seasonId);
        return await CheckAsync(store, season, token);
    }

    public async Task<CropSeasonResponse> CloseAsync(Guid seasonId, CloseCropSeasonRequest request, CancellationToken token)
    {
        var actor = await access.OwnerActorAsync("CROP_SEASONS.MANAGE", token);
        var store = await ActiveStore.GetIdAsync(db, token);
        await using (var tx = await db.BeginTransactionAsync(token))
        {
            // The store lock serializes closing with season changes and with rollovers into the season (they lock it too).
            await locks.LockStoreAsync(store, token);
            var season = await db.CropSeasons.SingleOrDefaultAsync(s => s.Id == seasonId && s.StoreId == store, token)
                ?? throw new NotFoundException("Crop season", seasonId);
            if (season.Version != request.Version) throw new ConflictException("The crop season changed. Reload it before closing.");
            var check = await CheckAsync(store, season, token);
            if (!check.CanClose)
            {
                throw new BusinessRuleException($"Season '{season.Code}' cannot be closed yet: {string.Join(", ", check.Reasons)}.");
            }

            var (origin, collection) = (await FiguresAsync(store, [season.Id], token))[season.Id];
            var summary = new ClosedSummary(origin, collection, Today);
            season.Close(actor, clock.UtcNow, JsonSerializer.Serialize(summary, Json), Today);
            audit.Record("CROP_SEASON_CLOSED", "CROP_SEASON", season.Id, store, newValues: summary,
                reason: string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }

        return await seasons.GetAsync(seasonId, token);
    }

    public async Task<PagedResult<DebtBySeasonItem>> ReportAsync(DebtBySeasonRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var query = db.CropSeasons.AsNoTracking().Where(s => s.StoreId == store);
        if (request.Year is { } year) query = query.Where(s => s.HarvestStart.Year == year);
        var total = await query.LongCountAsync(token);
        var page = await query.OrderByDescending(s => s.HarvestStart).ThenBy(s => s.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        var live = await FiguresAsync(store, page.Where(s => !s.IsClosed).Select(s => s.Id).ToList(), token);
        var today = Today;
        var items = page.Select(s =>
        {
            var frozen = s.ClosedSummary is { } text ? JsonSerializer.Deserialize<ClosedSummary>(text, Json) : null;
            var (origin, collection) = frozen is not null ? (frozen.Origin, frozen.Collection) : live[s.Id];
            return new DebtBySeasonItem(new(s.Id, s.Code, s.Name), s.HarvestStart, s.PaymentDueDate, EnumText.Format(s.PhaseOn(today)),
                s.ClosedAt, frozen is not null, origin, collection);
        }).ToList();
        return new(items, request.Page, request.PageSize, total);
    }

    // D12 / CS-14: the customers whose every debt that arose in each of the last N closed seasons (N from the debt policy)
    // was paid by real payments on or before its original due date, never rolled over, moved, reduced, forgiven or returned.
    public async Task<LimitSuggestionsResponse> LimitSuggestionsAsync(Guid seasonId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var chain = await db.CropSeasons.AsNoTracking().Where(s => s.StoreId == store).OrderBy(s => s.HarvestStart).ToListAsync(token);
        var index = chain.FindIndex(s => s.Id == seasonId);
        if (index < 0) throw new NotFoundException("Crop season", seasonId);
        if (!chain[index].IsClosed) throw new BusinessRuleException("Limit suggestions are only given for a closed season.");
        var policy = await db.StoreDebtPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store, token)
            ?? new StoreDebtPolicy(store);
        var required = policy.OnTimeSeasonsForLimitRaise;
        var window = index + 1 >= required ? chain.GetRange(index + 1 - required, required) : [];
        if (window.Count == 0 || window.Any(s => !s.IsClosed)) return new(seasonId, required, [], []);

        var windowIds = window.Select(s => s.Id).ToList();
        var entries = await (from e in db.DebtEntries.AsNoTracking()
                             join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                             where a.StoreId == store && e.OriginCropSeasonId != null && windowIds.Contains(e.OriginCropSeasonId.Value)
                             select new
                             {
                                 e.Id, Season = e.OriginCropSeasonId!.Value, a.FarmerProfileId, e.Status, e.RolloverCount, e.DueDate,
                                 e.OriginalDueDate, e.OriginalAmount,
                             }).ToListAsync(token);
        var entryIds = entries.Select(e => e.Id).ToList();
        var moves = await db.DebtTransactions.AsNoTracking()
            .Where(t => t.DebtEntryId != null && entryIds.Contains(t.DebtEntryId.Value) && t.Status == DebtTransactionStatus.Posted
                && (t.TransactionType == DebtTransactionType.Payment || t.TransactionType == DebtTransactionType.AdjustmentOut
                    || t.TransactionType == DebtTransactionType.Return))
            .Select(t => new { EntryId = t.DebtEntryId!.Value, t.TransactionType, t.OccurredAt }).ToListAsync(token);
        var byEntry = moves.ToLookup(m => m.EntryId);

        bool OnTime(Guid id, DebtEntryStatus status, int rollovers, DateOnly due, DateOnly original)
        {
            var list = byEntry[id].ToList();
            var payments = list.Where(m => m.TransactionType == DebtTransactionType.Payment).ToList();
            return status == DebtEntryStatus.Paid && rollovers == 0 && due == original && payments.Count > 0
                && list.All(m => m.TransactionType == DebtTransactionType.Payment)
                && BusinessCalendar.Today(payments.Max(m => m.OccurredAt)) <= original;
        }

        var qualified = entries.GroupBy(e => e.FarmerProfileId)
            .Where(g => windowIds.All(season => g.Any(e => e.Season == season))
                && g.All(e => OnTime(e.Id, e.Status, e.RolloverCount, e.DueDate, e.OriginalDueDate)))
            .ToDictionary(g => g.Key, g => (Paid: g.Sum(e => e.OriginalAmount),
                Last: BusinessCalendar.Today(g.SelectMany(e => byEntry[e.Id]).Max(m => m.OccurredAt))));
        var farmers = qualified.Keys.ToList();
        var profiles = await db.FarmerCreditProfiles.AsNoTracking()
            .Where(p => p.StoreId == store && farmers.Contains(p.FarmerProfileId))
            .Select(p => new { p.FarmerProfileId, p.FarmerProfile.User.FullName, p.FarmerProfile.User.PhoneNumber, p.CreditLimit, p.Status })
            .ToListAsync(token);
        return new(seasonId, required, window.Select(s => new DebtSeasonReference(s.Id, s.Code, s.Name)).ToList(),
            profiles.OrderBy(p => p.FullName).ThenBy(p => p.FarmerProfileId)
                .Select(p => new LimitSuggestionItem(p.FarmerProfileId, p.FullName, p.PhoneNumber, p.CreditLimit, EnumText.Format(p.Status),
                    qualified[p.FarmerProfileId].Paid, qualified[p.FarmerProfileId].Last)).ToList());
    }

    // D11: a collecting season with no debt still collected in it (disputed included) and no unfinished credit order of it.
    private async Task<CropSeasonClosingCheckResponse> CheckAsync(Guid store, CropSeason season, CancellationToken token)
    {
        var phase = season.PhaseOn(Today);
        var reasons = new List<string>();
        if (season.IsClosed) reasons.Add("ALREADY_CLOSED");
        else if (phase != CropSeasonPhase.Collecting) reasons.Add("HARVEST_NOT_ENDED");

        var openEntries = from e in db.DebtEntries.AsNoTracking()
                          join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                          where a.StoreId == store && e.CollectionCropSeasonId == season.Id && e.OutstandingAmount > 0
                          select new { e.Id, e.EntryNumber, a.FarmerProfileId, a.FarmerProfile.User.FullName, e.OutstandingAmount, e.Status };
        var entryCount = await openEntries.CountAsync(token);
        var entryList = await openEntries.OrderBy(e => e.EntryNumber).Take(ListLimit).ToListAsync(token);
        var openOrders = db.Orders.AsNoTracking()
            .Where(o => o.StoreId == store && o.CropSeasonId == season.Id && OpenOrderStatuses.Contains(o.Status));
        var orderCount = await openOrders.CountAsync(token);
        var orderList = await openOrders.OrderBy(o => o.OrderNumber).Take(ListLimit)
            .Select(o => new { o.Id, o.OrderNumber, o.Status }).ToListAsync(token);
        if (!season.IsClosed && entryCount > 0) reasons.Add("OPEN_ENTRIES");
        if (!season.IsClosed && orderCount > 0) reasons.Add("OPEN_CREDIT_ORDERS");

        return new(season.Id, EnumText.Format(phase), reasons.Count == 0, reasons, entryCount,
            entryList.Select(e => new SeasonOpenEntry(e.Id, e.EntryNumber, e.FarmerProfileId, e.FullName, e.OutstandingAmount,
                EnumText.Format(e.Status))).ToList(),
            orderCount, orderList.Select(o => new SeasonOpenOrder(o.Id, o.OrderNumber, EnumText.Format(o.Status))).ToList());
    }

    // Live figures of the given seasons, from the entries and their posted ledger lines.
    private async Task<Dictionary<Guid, (SeasonOriginFigures Origin, SeasonCollectionFigures Collection)>> FiguresAsync(
        Guid store, List<Guid> ids, CancellationToken token)
    {
        var entries = from e in db.DebtEntries.AsNoTracking()
                      join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                      where a.StoreId == store
                      select new { Entry = e, a.FarmerProfileId };
        var origin = await entries.Where(x => x.Entry.OriginCropSeasonId != null && ids.Contains(x.Entry.OriginCropSeasonId.Value))
            .GroupBy(x => x.Entry.OriginCropSeasonId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                Originated = g.Sum(x => x.Entry.OriginalAmount),
                Outstanding = g.Sum(x => x.Entry.OutstandingAmount),
                CarriedOver = g.Sum(x => x.Entry.CollectionCropSeasonId != x.Entry.OriginCropSeasonId ? x.Entry.OutstandingAmount : 0m),
                Entries = g.Count(),
            }).ToDictionaryAsync(x => x.Id, token);
        var customers = (await entries.Where(x => x.Entry.OriginCropSeasonId != null && ids.Contains(x.Entry.OriginCropSeasonId.Value))
                .Select(x => new { Season = x.Entry.OriginCropSeasonId!.Value, x.FarmerProfileId }).Distinct().ToListAsync(token))
            .GroupBy(x => x.Season).ToDictionary(g => g.Key, g => g.Count());
        var moves = await (from t in db.DebtTransactions.AsNoTracking()
                           join x in entries on t.DebtEntryId equals (Guid?)x.Entry.Id
                           where x.Entry.OriginCropSeasonId != null && ids.Contains(x.Entry.OriginCropSeasonId.Value)
                               && t.Status == DebtTransactionStatus.Posted
                               && (t.TransactionType == DebtTransactionType.Payment || t.TransactionType == DebtTransactionType.Return
                                   || t.TransactionType == DebtTransactionType.AdjustmentOut)
                           group t by new { Season = x.Entry.OriginCropSeasonId!.Value, t.TransactionType, t.DebtEntryActionId } into g
                           select new { g.Key.Season, g.Key.TransactionType, g.Key.DebtEntryActionId, Amount = g.Sum(t => -t.AmountDelta) })
            .ToListAsync(token);
        var actionIds = moves.Where(m => m.DebtEntryActionId != null).Select(m => m.DebtEntryActionId!.Value).Distinct().ToList();
        var cancels = (await db.DebtEntryActions.AsNoTracking()
            .Where(a => actionIds.Contains(a.Id) && a.ActionType == DebtEntryActionType.Cancel).Select(a => a.Id).ToListAsync(token)).ToHashSet();
        var collection = await entries.Where(x => x.Entry.CollectionCropSeasonId != null && ids.Contains(x.Entry.CollectionCropSeasonId.Value))
            .GroupBy(x => x.Entry.CollectionCropSeasonId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                ToCollect = g.Sum(x => x.Entry.OutstandingAmount),
                CarriedIn = g.Sum(x => x.Entry.OriginCropSeasonId != x.Entry.CollectionCropSeasonId ? x.Entry.OutstandingAmount : 0m),
                Open = g.Sum(x => x.Entry.OutstandingAmount > 0 ? 1 : 0),
            }).ToDictionaryAsync(x => x.Id, token);

        return ids.ToDictionary(id => id, id =>
        {
            var seasonMoves = moves.Where(m => m.Season == id).ToList();
            decimal Sum(DebtTransactionType type, Func<Guid?, bool>? action = null) => seasonMoves
                .Where(m => m.TransactionType == type && (action is null || action(m.DebtEntryActionId))).Sum(m => m.Amount);
            var o = origin.GetValueOrDefault(id);
            var c = collection.GetValueOrDefault(id);
            return (new SeasonOriginFigures(o?.Originated ?? 0, Sum(DebtTransactionType.Payment), Sum(DebtTransactionType.Return),
                    Sum(DebtTransactionType.AdjustmentOut, a => a is null || !cancels.Contains(a.Value)),
                    Sum(DebtTransactionType.AdjustmentOut, a => a is not null && cancels.Contains(a.Value)),
                    o?.CarriedOver ?? 0, o?.Outstanding ?? 0, o?.Entries ?? 0, customers.GetValueOrDefault(id)),
                new SeasonCollectionFigures(c?.CarriedIn ?? 0, c?.ToCollect ?? 0, c?.Open ?? 0));
        });
    }
}
