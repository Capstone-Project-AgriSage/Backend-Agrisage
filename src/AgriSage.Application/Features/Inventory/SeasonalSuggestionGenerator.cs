using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.Extensions.Options;

namespace AgriSage.Application.Features.Inventory;

// A season to suggest for, its candidates and the aggregated data they came from.
public sealed record SeasonalSuggestionPlan(
    SeasonalSuggestionSeason Season,
    string CropName,
    SeasonalCandidateSet Candidates,
    string CandidatesJson);

// Prepares and ranks a seasonal suggestion (F4.7). Preparing reads data only; ranking is the fixed data score. No AI
// is involved and nothing is saved here.
public sealed class SeasonalSuggestionGenerator(
    SeasonalSuggestionCandidates candidates,
    IOptions<SeasonalSuggestionOptions> options,
    IDateTimeProvider clock)
{
    public async Task<SeasonalSuggestionPlan> PrepareAsync(
        Guid store,
        string crop,
        string? region,
        DateOnly startsOn,
        DateOnly endsOn,
        int prepareDays,
        CancellationToken token)
    {
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var (from, to) = SeasonalSuggestionRules.ComparedWindow(startsOn, endsOn, today);
        var season = new SeasonalSuggestionSeason(crop, region, startsOn, endsOn, prepareDays, from, to);
        var set = await candidates.BuildAsync(store, crop, from, to, today, now, options.Value, token);
        var cropName = CropCatalog.Find(crop)?.Name ?? crop;
        var json = JsonSerializer.Serialize(new
        {
            problems = set.Problems.Select(p => new { p.Name, p.Kind, p.CasesInWindow, p.CasesLastYear }),
            candidates = set.Candidates.Select(c => new
            {
                c.Code, c.ProductId, c.StoreProductId, c.Sku, c.Name, c.Category, Group = EnumText.Format(c.Group), c.Score,
                c.Sold, c.SaleDays, c.ObservedDays, c.Sellable, c.Advised, PastRule = c.PastRule?.Name,
                Problems = c.LinkedProblems.Select(p => p.Name), c.ActiveIngredients
            })
        }, SeasonalSuggestionRules.Json);
        return new SeasonalSuggestionPlan(season, cropName, set, json);
    }

    // The best-scored candidates that have a data signal, in score order.
    public IReadOnlyList<SeasonalSuggestionLine> Rank(SeasonalSuggestionPlan plan) =>
        plan.Candidates.Candidates.Where(c => c.Score > 0).Take(options.Value.MaxItems)
            .Select(c => Line(plan, c, SeasonalSuggestionRules.DataConfidence(c.Sold, c.LinkedProblems.Count > 0,
                c.ProblemCases, c.PastRule is not null)))
            .ToList();

    private static SeasonalSuggestionLine Line(SeasonalSuggestionPlan plan, SeasonalCandidate c, SeasonalSuggestionConfidence confidence)
    {
        var season = plan.Season;
        var reason = SeasonalSuggestionRules.DataReason(c.Sold, c.BaseUnitName,
            c.LinkedProblems.Select(p => p.Name).ToList(), c.Advised, c.PastRule?.Name, c.InStore);
        var evidence = SeasonalSuggestionRules.Evidence(c.InStore, c.Sold, c.BaseUnitName, season.ComparedFrom,
            season.ComparedTo, plan.CropName,
            c.LinkedProblems.Select(p => new SeasonalSuggestionRules.ProblemCount(p.Name, p.CasesInWindow)).ToList(),
            c.Advised, c.PastRule, c.Sellable);
        var target = c.InStore
            ? SeasonalSuggestionRules.SuggestedTarget(c.Sold, c.SaleDays, c.ObservedDays, c.Minimum, c.LeadDays, c.CoverageDays, c.Safety)
            : null;
        return new SeasonalSuggestionLine(
            c.ProductId,
            c.StoreProductId,
            c.Group,
            confidence,
            reason,
            JsonSerializer.Serialize(evidence, SeasonalSuggestionRules.Json),
            c.InStore ? c.Sold : null,
            c.InStore ? c.Sellable ?? 0 : null,
            target,
            SeasonalSuggestionRules.OrderByDate(season.StartsOn, season.PrepareDays, c.LeadDays));
    }
}
