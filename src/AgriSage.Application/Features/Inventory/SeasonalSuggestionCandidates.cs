using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// An active problem of the crop and how often it was found (diagnosis cases with a final problem).
public sealed record SeasonalProblem(Guid Id, string Name, string Kind, int CasesInWindow, int CasesLastYear);

// One product the ranking may choose from, with its aggregated signals (F4.7 rule 2). Code is a short name kept in
// the run's candidate data (P001, …). Store fields are null for a product the store does not carry.
public sealed record SeasonalCandidate(
    string Code,
    Guid ProductId,
    Guid? StoreProductId,
    string Sku,
    string Name,
    string? Category,
    string? Brand,
    string? Note,
    IReadOnlyList<string> ActiveIngredients,
    string? BaseUnit,
    string? BaseUnitName,
    long? Sold,
    int SaleDays,
    int ObservedDays,
    long? Sellable,
    IReadOnlyList<SeasonalProblem> LinkedProblems,
    int Advised,
    SeasonalSuggestionRules.RuleMention? PastRule,
    SeasonalProductGroup Group,
    int Score,
    long? Minimum,
    int LeadDays,
    int CoverageDays,
    long Safety)
{
    public bool InStore => StoreProductId is not null;

    public int ProblemCases => LinkedProblems.Sum(p => p.CasesInWindow);
}

public sealed record SeasonalCandidateSet(IReadOnlyList<SeasonalCandidate> Candidates, IReadOnlyList<SeasonalProblem> Problems);

// Chooses the candidates of a season from data only (FLOW_4 "Seasonal product suggestions", rule 2): the Store's
// active, sellable products with a signal, and catalog products it does not carry that have an approved link for the
// crop. Crop protection needs an approved link for the crop. Ordered by the fixed score.
public sealed class SeasonalSuggestionCandidates(IAgriSageDbContext context, InventoryPlanningData planning)
{
    private const int NoteLength = 200;

    public async Task<SeasonalCandidateSet> BuildAsync(
        Guid store,
        string crop,
        DateOnly comparedFrom,
        DateOnly comparedTo,
        DateOnly today,
        DateTimeOffset now,
        SeasonalSuggestionOptions options,
        CancellationToken token)
    {
        var windowStart = BusinessCalendar.StartOfDay(comparedFrom);
        var windowEnd = BusinessCalendar.StartOfDay(comparedTo.AddDays(1));
        var yearStart = now.AddDays(-365);

        // Problems of the crop and how often each was found.
        var problemRows = await context.Diseases.AsNoTracking()
            .Where(d => d.IsActive && !d.IsHealthyClass && d.CropType == crop)
            .Select(d => new { d.Id, d.Name, d.ProblemType }).ToListAsync(token);
        var problemIds = problemRows.Select(p => p.Id).ToArray();
        var cases = await context.DiagnosisCases.AsNoTracking()
            .Where(c => c.FinalDiseaseId != null && problemIds.Contains(c.FinalDiseaseId.Value)
                && (c.SubmittedAt >= yearStart || (c.SubmittedAt >= windowStart && c.SubmittedAt < windowEnd)))
            .GroupBy(c => c.FinalDiseaseId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                InWindow = g.Count(c => c.SubmittedAt >= windowStart && c.SubmittedAt < windowEnd),
                LastYear = g.Count(c => c.SubmittedAt >= yearStart)
            })
            .ToListAsync(token);
        var problems = problemRows.Select(p =>
        {
            var count = cases.SingleOrDefault(c => c.Id == p.Id);
            return new SeasonalProblem(p.Id, p.Name, EnumText.Format(p.ProblemType), count?.InWindow ?? 0, count?.LastYear ?? 0);
        }).OrderByDescending(p => p.CasesInWindow).ThenByDescending(p => p.CasesLastYear).ThenBy(p => p.Name).ToList();
        var problemById = problems.ToDictionary(p => p.Id);

        // Approved links for the crop's problems.
        var links = await context.DiseaseProducts.AsNoTracking()
            .Where(l => l.Status == DiseaseProductStatus.Approved && problemIds.Contains(l.DiseaseId))
            .Select(l => new { l.ProductId, l.DiseaseId, l.RecommendationGroup }).ToListAsync(token);
        var linksByProduct = links.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        // The Store's products: every one it carries (to tell what it does not), and the active, sellable ones in detail.
        var carried = (await context.StoreProducts.AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => p.ProductId).ToListAsync(token)).ToHashSet();
        var storeRows = await context.StoreProducts.AsNoTracking()
            .Where(p => p.StoreId == store && p.IsActive && p.IsSellable && p.Product.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.ProductId,
                Sku = p.StoreSku ?? p.Product.Sku,
                p.Product.Name,
                Category = p.Product.Category.Name,
                Brand = p.Product.Brand != null ? p.Product.Brand.Name : null,
                Note = p.Product.Description ?? p.Product.UsageInstructions,
                Unit = p.Product.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Code).FirstOrDefault(),
                UnitName = p.Product.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Name).FirstOrDefault(),
                p.MinStockLevelBase,
                p.LeadTimeDays,
                p.TargetCoverageDays,
                p.SafetyStockBase
            })
            .ToListAsync(token);
        var notCarriedIds = linksByProduct.Keys.Where(id => !carried.Contains(id)).ToArray();
        var notCarriedRows = await context.Products.AsNoTracking()
            .Where(p => notCarriedIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                Category = p.Category.Name,
                Brand = p.Brand != null ? p.Brand.Name : null,
                Note = p.Description ?? p.UsageInstructions,
                Unit = p.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Code).FirstOrDefault(),
                UnitName = p.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Name).FirstOrDefault()
            })
            .ToListAsync(token);

        // What tells crop protection apart: an active ingredient or a PROTECTION link for any crop.
        var productIds = storeRows.Select(p => p.ProductId).Concat(notCarriedRows.Select(p => p.Id)).Distinct().ToArray();
        var ingredients = (await context.ProductActiveIngredients.AsNoTracking()
                .Where(i => productIds.Contains(i.ProductId))
                .Select(i => new { i.ProductId, i.ActiveIngredient.Name }).ToListAsync(token))
            .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Select(i => i.Name).Distinct().OrderBy(n => n).ToList());
        var protectionAnyCrop = (await context.DiseaseProducts.AsNoTracking()
                .Where(l => l.RecommendationGroup == RecommendationGroup.Protection && productIds.Contains(l.ProductId))
                .Select(l => l.ProductId).Distinct().ToListAsync(token))
            .ToHashSet();

        // Signals of the Store's products: sales in the history window, stock now, advice and last seasons' rules.
        var storeProductIds = storeRows.Select(p => p.Id).ToArray();
        var sales = (await planning.SalesBetweenAsync(store, storeProductIds, windowStart, windowEnd, token))
            .ToDictionary(s => s.ProductId);
        var sellable = (await context.InventoryLots.AsNoTracking()
                .Where(l => storeProductIds.Contains(l.StoreProductId) && l.Status == InventoryLotStatus.Active
                    && (l.ExpiryDate == null || l.ExpiryDate >= today))
                .GroupBy(l => l.StoreProductId)
                .Select(g => new { Id = g.Key, Quantity = g.Sum(l => l.Balance.QuantityOnHand - l.Balance.QuantityReserved) })
                .ToListAsync(token))
            .ToDictionary(s => s.Id, s => Math.Max(0, s.Quantity));
        var advised = (await (from item in context.AdvisorySessionItems.AsNoTracking()
                              join session in context.AdvisorySessions.AsNoTracking() on item.AdvisorySessionId equals session.Id
                              where session.StoreId == store && problemIds.Contains(session.DiseaseId) && session.CreatedAt >= yearStart
                              group item by item.StoreProductId into g
                              select new { Id = g.Key, Count = g.Count() }).ToListAsync(token))
            .ToDictionary(a => a.Id, a => a.Count);
        var cropName = CropCatalog.Find(crop)?.Name;
        var since = today.AddYears(-2);
        var rules = (await (from item in context.SeasonalRuleItems.AsNoTracking()
                            join rule in context.SeasonalRules.AsNoTracking() on item.SeasonalRuleId equals rule.Id
                            where rule.StoreId == store && rule.EndsOn >= since
                                && (rule.CropType.ToUpper() == crop || (cropName != null && rule.CropType.ToLower() == cropName.ToLower()))
                            select new { item.StoreProductId, rule.Name, rule.StartsOn, rule.EndsOn }).ToListAsync(token))
            .GroupBy(r => r.StoreProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.StartsOn).Select(r =>
                new SeasonalSuggestionRules.RuleMention(r.Name, r.StartsOn, r.EndsOn)).First());

        var fromStore = new List<SeasonalCandidate>();
        foreach (var p in storeRows)
        {
            var productLinks = linksByProduct.GetValueOrDefault(p.ProductId) ?? [];
            var linkedProblems = productLinks.Select(l => problemById[l.DiseaseId]).Distinct().ToList();
            var productIngredients = ingredients.GetValueOrDefault(p.ProductId) ?? [];
            var isProtection = SeasonalSuggestionRules.IsCropProtection(productIngredients.Count > 0,
                protectionAnyCrop.Contains(p.ProductId), p.Category);
            if (!SeasonalSuggestionRules.IsEligible(isProtection, productLinks.Count > 0)) continue;

            var sale = sales.GetValueOrDefault(p.Id);
            var sold = Math.Max(0, sale?.NetQuantity ?? 0);
            var advice = advised.GetValueOrDefault(p.Id);
            var pastRule = rules.GetValueOrDefault(p.Id);
            if (sold <= 0 && productLinks.Count == 0 && advice == 0 && pastRule is null) continue;

            var firstDay = sale?.FirstMovementAt is { } first ? BusinessCalendar.Today(first) : (DateOnly?)null;
            var group = SeasonalSuggestionRules.Group(
                productLinks.Any(l => l.RecommendationGroup == RecommendationGroup.Protection),
                productLinks.Any(l => l.RecommendationGroup == RecommendationGroup.Nutrition), isProtection, p.Category);
            fromStore.Add(new SeasonalCandidate("", p.ProductId, p.Id, p.Sku, p.Name, p.Category, p.Brand, Short(p.Note),
                productIngredients, p.Unit, p.UnitName, sold, sale?.SaleDays ?? 0,
                SeasonalSuggestionRules.ObservedDays(comparedFrom, comparedTo, firstDay), sellable.GetValueOrDefault(p.Id),
                linkedProblems, advice, pastRule, group,
                SeasonalSuggestionRules.Score(sold, productLinks.Count > 0, linkedProblems.Sum(l => l.CasesInWindow), advice,
                    pastRule is not null, inStore: true),
                p.MinStockLevelBase, p.LeadTimeDays, p.TargetCoverageDays, p.SafetyStockBase));
        }

        var notCarried = notCarriedRows.Select(p =>
        {
            var productLinks = linksByProduct[p.Id];
            var linkedProblems = productLinks.Select(l => problemById[l.DiseaseId]).Distinct().ToList();
            var productIngredients = ingredients.GetValueOrDefault(p.Id) ?? [];
            var isProtection = SeasonalSuggestionRules.IsCropProtection(productIngredients.Count > 0,
                protectionAnyCrop.Contains(p.Id), p.Category);
            var group = SeasonalSuggestionRules.Group(
                productLinks.Any(l => l.RecommendationGroup == RecommendationGroup.Protection),
                productLinks.Any(l => l.RecommendationGroup == RecommendationGroup.Nutrition), isProtection, p.Category);
            return new SeasonalCandidate("", p.Id, null, p.Sku, p.Name, p.Category, p.Brand, Short(p.Note), productIngredients,
                p.Unit, p.UnitName, null, 0, 0, null, linkedProblems, 0, null, group,
                SeasonalSuggestionRules.Score(null, true, linkedProblems.Sum(l => l.CasesInWindow), 0, false, inStore: false),
                null, SeasonalSuggestionRules.DefaultLeadDays, 0, 0);
        }).ToList();

        var notCarriedKept = Order(notCarried).Take(Math.Min(options.MaxNotCarried, options.MaxCandidates)).ToList();
        var storeKept = Order(fromStore).Take(options.MaxCandidates - notCarriedKept.Count).ToList();
        var ordered = Order(storeKept.Concat(notCarriedKept))
            .Select((candidate, index) => candidate with { Code = $"P{index + 1:000}" })
            .ToList();
        return new SeasonalCandidateSet(ordered, problems);
    }

    private static IOrderedEnumerable<SeasonalCandidate> Order(IEnumerable<SeasonalCandidate> candidates) =>
        candidates.OrderByDescending(c => c.Score).ThenByDescending(c => c.Sold ?? -1).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ThenBy(c => c.ProductId);

    private static string? Short(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        var text = string.Join(' ', note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= NoteLength ? text : text[..(NoteLength - 1)].TrimEnd() + "…";
    }
}
