using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// F4.7 seasonal product suggestions (FLOW_4 "Seasonal product suggestions"): reads the runs the reminder prepared and
// fills in a seasonal rule from one of them. Nothing here changes data.
public sealed class SeasonalSuggestionService(IAgriSageDbContext context, InventoryPlanningData planning) : ISeasonalSuggestionService
{
    public const string FactorFromLastSeason = "LAST_SEASON";
    public const string FactorDefault = "DEFAULT";
    public const string SkippedNotCarried = "NOT_CARRIED";

    public async Task<PagedResult<SeasonalSuggestionListItem>> ListAsync(SeasonalSuggestionListRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var query = context.SeasonalSuggestionRuns.AsNoTracking().Where(r => r.StoreId == store);
        if (CropCatalog.Normalize(request.CropType) is { } crop) query = query.Where(r => r.CropType == crop);
        if (request.FromDate is { } from) query = query.Where(r => r.EndsOn >= from);
        if (request.ToDate is { } to) query = query.Where(r => r.StartsOn <= to);

        var total = await query.LongCountAsync(token);
        var rows = await query.OrderBy(r => r.StartsOn).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(r => new { r.Id, r.CropType, r.StartsOn, r.EndsOn, r.CreatedAt, ItemCount = r.Items.Count() })
            .ToListAsync(token);
        IReadOnlyList<ActiveRule> rules = rows.Count == 0 ? [] : await ActiveRulesAsync(store, token);
        return new PagedResult<SeasonalSuggestionListItem>(rows.Select(r => new SeasonalSuggestionListItem(
            r.Id, r.CropType, CropName(r.CropType), r.StartsOn, r.EndsOn, r.ItemCount, r.CreatedAt,
            Covering(rules, r.CropType, r.StartsOn, r.EndsOn))).ToList(),
            request.Page, request.PageSize, total);
    }

    public async Task<SeasonalSuggestionResponse> GetAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        return await ReadAsync(store, id, token) ?? throw new NotFoundException("Seasonal suggestion", id);
    }

    // FLOW_4 "Seasonal product suggestions", rule 6: the season's crop, days and preparation, the suggested products the
    // store carries with their suggested target, and a demand factor from last year's sales (1.5 without history).
    public async Task<SeasonalRuleDraftResponse> RuleDraftAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var run = await ReadAsync(store, id, token) ?? throw new NotFoundException("Seasonal suggestion", id);
        var carried = run.Items.Where(i => i.StoreProductId is not null).ToList();
        var (baselineFrom, baselineTo) = SeasonalSuggestionRules.BaselineWindow(run.ComparedFrom);
        var factor = await DemandFactorAsync(store, carried.Select(i => i.StoreProductId!.Value).ToList(),
            run.ComparedFrom, run.ComparedTo, baselineFrom, baselineTo, token);
        return new SeasonalRuleDraftResponse(
            run.Id, SeasonalSuggestionRules.RuleName(run.CropName, run.StartsOn, run.EndsOn), run.CropType, run.CropName,
            run.Region, run.StartsOn, run.EndsOn, run.PrepareDays,
            factor.Factor, factor.FromHistory ? FactorFromLastSeason : FactorDefault, factor.Products,
            run.ComparedFrom, run.ComparedTo, baselineFrom, baselineTo, run.CoveringRule,
            carried.Select(i => new SeasonalRuleDraftLine(i.StoreProductId!.Value, i.Sku, i.ProductName, i.BaseUnit,
                i.SuggestedTargetBase)).ToList(),
            run.Items.Where(i => i.StoreProductId is null)
                .Select(i => new SeasonalRuleDraftSkipped(i.ProductId, i.Sku, i.ProductName, SkippedNotCarried)).ToList());
    }

    private static string CropName(string code) => CropCatalog.Find(code)?.Name ?? code;

    // Daily demand of each product in last year's season and in the 90 days before it, by the forecast's rules
    // (posted SALE less RETURN_IN and their reversals; ≥ 14 observed days and ≥ 3 sale days).
    private async Task<SeasonalSuggestionRules.DemandFactorResult> DemandFactorAsync(Guid store, IReadOnlyList<Guid> products,
        DateOnly seasonFrom, DateOnly seasonTo, DateOnly baselineFrom, DateOnly baselineTo, CancellationToken token)
    {
        if (products.Count == 0) return SeasonalSuggestionRules.DemandFactor([]);
        var season = (await planning.SalesBetweenAsync(store, products, BusinessCalendar.StartOfDay(seasonFrom),
            BusinessCalendar.StartOfDay(seasonTo.AddDays(1)), token)).ToDictionary(s => s.ProductId);
        var baseline = (await planning.SalesBetweenAsync(store, products, BusinessCalendar.StartOfDay(baselineFrom),
            BusinessCalendar.StartOfDay(baselineTo.AddDays(1)), token)).ToDictionary(s => s.ProductId);
        return SeasonalSuggestionRules.DemandFactor(products.Select(p => (
            Rate(season.GetValueOrDefault(p), seasonFrom, seasonTo),
            Rate(baseline.GetValueOrDefault(p), baselineFrom, baselineTo))));
    }

    private static decimal? Rate(InventoryPlanningData.WindowSale? sale, DateOnly from, DateOnly to)
    {
        if (sale is null) return null;
        var first = sale.FirstMovementAt is { } at ? BusinessCalendar.Today(at) : (DateOnly?)null;
        return ReplenishmentMath.DailyDemand(Math.Max(0, sale.NetQuantity),
            SeasonalSuggestionRules.ObservedDays(from, to, first), sale.SaleDays);
    }

    private sealed record ActiveRule(Guid Id, string Name, string? Crop, DateOnly StartsOn, DateOnly EndsOn);

    // The Store's active seasonal rules, crops read as catalog codes the way the reminder reads them.
    private async Task<IReadOnlyList<ActiveRule>> ActiveRulesAsync(Guid store, CancellationToken token) =>
        (await context.SeasonalRules.AsNoTracking().Where(r => r.StoreId == store && r.IsActive)
            .OrderBy(r => r.StartsOn).ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Name, r.CropType, r.StartsOn, r.EndsOn }).ToListAsync(token))
        .Select(r => new ActiveRule(r.Id, r.Name, SeasonalSuggestionReminder.KnownCrop(r.CropType), r.StartsOn, r.EndsOn))
        .ToList();

    // The same test the reminder uses before it notifies: an active rule of the crop whose days overlap the season.
    private static SeasonalCoveringRule? Covering(IReadOnlyList<ActiveRule> rules, string crop, DateOnly startsOn, DateOnly endsOn) =>
        rules.Where(r => r.Crop == crop && r.StartsOn <= endsOn && r.EndsOn >= startsOn)
            .Select(r => new SeasonalCoveringRule(r.Id, r.Name)).FirstOrDefault();

    private async Task<SeasonalSuggestionResponse?> ReadAsync(Guid store, Guid id, CancellationToken token)
    {
        var run = await context.SeasonalSuggestionRuns.AsNoTracking().Where(r => r.Id == id && r.StoreId == store)
            .Select(r => new
            {
                r.Id, r.CropType, r.Region, r.StartsOn, r.EndsOn, r.PrepareDays, r.ComparedFrom, r.ComparedTo,
                r.CandidateCount, r.SeasonalRuleId, r.CreatedAt
            })
            .SingleOrDefaultAsync(token);
        if (run is null) return null;

        // Products keep their row even if later deleted from the catalog; "in store" is read now, so a product added
        // to the store after the run shows as carried.
        var items = await (from i in context.SeasonalSuggestionItems.AsNoTracking()
                           join p in context.Products.IgnoreQueryFilters().AsNoTracking() on i.ProductId equals p.Id
                           where i.RunId == id
                           orderby i.Rank
                           select new
                           {
                               i.Id, i.Rank, i.ProductId, i.ProductGroup, i.Confidence, i.Reason, i.Evidence,
                               i.SoldLastSeasonBase, i.SellableBase, i.SuggestedTargetBase, i.OrderByDate,
                               p.Sku, p.Name,
                               BaseUnit = p.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Code).FirstOrDefault(),
                               Current = context.StoreProducts.Where(s => s.StoreId == store && s.ProductId == i.ProductId)
                                   .Select(s => new { s.Id, s.StoreSku }).FirstOrDefault()
                           }).ToListAsync(token);
        var covering = Covering(await ActiveRulesAsync(store, token), run.CropType, run.StartsOn, run.EndsOn);
        return new SeasonalSuggestionResponse(
            run.Id, run.CropType, CropName(run.CropType), run.Region, run.StartsOn, run.EndsOn, run.PrepareDays,
            run.ComparedFrom, run.ComparedTo, run.CandidateCount, run.SeasonalRuleId, run.CreatedAt,
            items.Select(i => new SeasonalSuggestionItemResponse(
                i.Id, i.Rank, i.ProductId, i.Current?.Id, i.Current is not null, i.Current?.StoreSku ?? i.Sku, i.Name,
                i.BaseUnit ?? "", EnumText.Format(i.ProductGroup), EnumText.Format(i.Confidence), i.Reason, ReadEvidence(i.Evidence),
                i.SoldLastSeasonBase, i.SellableBase, i.SuggestedTargetBase, i.OrderByDate)).ToList(),
            covering);
    }

    private static IReadOnlyList<SeasonalSuggestionEvidence> ReadEvidence(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SeasonalSuggestionEvidence>>(json, SeasonalSuggestionRules.Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
