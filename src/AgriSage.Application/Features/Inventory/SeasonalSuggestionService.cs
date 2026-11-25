using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Diagnosis;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// F4.7 seasonal product suggestions (FLOW_4 "Seasonal product suggestions"): reads the runs the reminder prepared.
// Nothing here changes data.
public sealed class SeasonalSuggestionService(IAgriSageDbContext context) : ISeasonalSuggestionService
{
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
        return new PagedResult<SeasonalSuggestionListItem>(rows.Select(r => new SeasonalSuggestionListItem(
            r.Id, r.CropType, CropName(r.CropType), r.StartsOn, r.EndsOn, r.ItemCount, r.CreatedAt)).ToList(),
            request.Page, request.PageSize, total);
    }

    public async Task<SeasonalSuggestionResponse> GetAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        return await ReadAsync(store, id, token) ?? throw new NotFoundException("Seasonal suggestion", id);
    }

    private static string CropName(string code) => CropCatalog.Find(code)?.Name ?? code;

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
        return new SeasonalSuggestionResponse(
            run.Id, run.CropType, CropName(run.CropType), run.Region, run.StartsOn, run.EndsOn, run.PrepareDays,
            run.ComparedFrom, run.ComparedTo, run.CandidateCount, run.SeasonalRuleId, run.CreatedAt,
            items.Select(i => new SeasonalSuggestionItemResponse(
                i.Id, i.Rank, i.ProductId, i.Current?.Id, i.Current is not null, i.Current?.StoreSku ?? i.Sku, i.Name,
                i.BaseUnit ?? "", EnumText.Format(i.ProductGroup), EnumText.Format(i.Confidence), i.Reason, ReadEvidence(i.Evidence),
                i.SoldLastSeasonBase, i.SellableBase, i.SuggestedTargetBase, i.OrderByDate)).ToList());
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
