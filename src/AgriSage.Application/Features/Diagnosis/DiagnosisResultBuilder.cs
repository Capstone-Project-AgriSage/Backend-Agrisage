using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Products;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

// The one place that decides what the Farmer reads for a case (AI_DIAGNOSIS.md 2.0, section 7): the latest successful
// inference as a CONFIDENT or UNCERTAIN result, or the review of a case decided before 2.0. Products are suggested by
// active ingredient at read time (D13) and nothing is stored, so the answer follows the catalog as it is now.
public sealed class DiagnosisResultBuilder(IAgriSageDbContext context)
{
    public const string UncertainHint = "RETAKE_PHOTO_OR_ASK_STORE";

    public sealed record LatestInference(Guid? PredictedDiseaseId, bool PassedPolicy);

    // The latest SUCCESS inference of each case; a case without one is absent.
    public async Task<IReadOnlyDictionary<Guid, LatestInference>> LatestAsync(
        IReadOnlyCollection<Guid> caseIds, CancellationToken cancellationToken)
    {
        var rows = await context.AiInferences.AsNoTracking()
            .Where(i => caseIds.Contains(i.DiagnosisCaseId) && i.Status == AiInferenceStatus.Success)
            .Select(i => new { i.Id, i.DiagnosisCaseId, i.PredictedDiseaseId, i.PassedPolicy, i.InferredAt })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.DiagnosisCaseId).ToDictionary(
            g => g.Key,
            g =>
            {
                var latest = g.OrderByDescending(r => r.InferredAt).ThenByDescending(r => r.Id).First();

                return new LatestInference(latest.PredictedDiseaseId, latest.PassedPolicy);
            });
    }

    // Null when there is nothing to show yet (or any more): see DiagnosisResultPolicy.CertaintyOf.
    public async Task<DiagnosisResultResponse?> ForCaseAsync(
        Guid caseId, DiagnosisCaseStatus status, Disease? finalDisease, CancellationToken cancellationToken)
    {
        if (status == DiagnosisCaseStatus.Verified)
        {
            return finalDisease is null ? null : await ReviewedAsync(caseId, finalDisease, cancellationToken);
        }

        var latest = (await LatestAsync([caseId], cancellationToken)).GetValueOrDefault(caseId);
        if (DiagnosisResultPolicy.CertaintyOf(status, latest?.PassedPolicy) is not { } certainty
            || latest?.PredictedDiseaseId is not { } diseaseId)
        {
            return null;
        }

        var disease = await context.Diseases.AsNoTracking().FirstOrDefaultAsync(d => d.Id == diseaseId, cancellationToken);
        if (disease is null)
        {
            return null;
        }

        var treatments = (await context.DiseaseTreatments.AsNoTracking()
                .Where(t => t.DiseaseId == disease.Id && t.IsActive)
                .OrderByDescending(t => t.Priority).ThenBy(t => t.Title).ThenBy(t => t.Id)
                .Select(t => new
                {
                    t.Id, t.TreatmentType, t.Title, t.Instructions, t.Precautions,
                    Ingredient = t.ActiveIngredient != null ? t.ActiveIngredient.Name : null
                })
                .ToListAsync(cancellationToken))
            .Where(t => DiagnosisResultPolicy.ShowsTreatment(certainty, t.TreatmentType))
            .Select((t, index) => new TreatmentResponse(
                t.Id, EnumText.Format(t.TreatmentType), t.Title, t.Instructions, t.Precautions, index + 1, null, t.Ingredient))
            .ToList();

        var products = DiagnosisResultPolicy.OffersProducts(certainty, disease.IsHealthyClass)
            ? await SuggestAsync(disease.Id, cancellationToken)
            : [];

        return new DiagnosisResultResponse(
            Summary(disease), null, treatments, products, EnumText.Format(certainty),
            certainty == DiagnosisCertainty.Uncertain ? UncertainHint : null);
    }

    // Store products the Farmer can buy whose product contains an active ingredient of an active treatment of the
    // disease. Order: the best matching treatment's priority, then more matched ingredients, then name.
    public async Task<IReadOnlyList<RecommendedProductResponse>> SuggestAsync(Guid diseaseId, CancellationToken cancellationToken)
    {
        var wanted = await context.DiseaseTreatments.AsNoTracking()
            .Where(t => t.DiseaseId == diseaseId && t.IsActive && t.ActiveIngredientId != null && t.ActiveIngredient!.IsActive)
            .Select(t => new { IngredientId = t.ActiveIngredientId!.Value, t.ActiveIngredient!.Name, t.Priority })
            .ToListAsync(cancellationToken);
        if (wanted.Count == 0)
        {
            return [];
        }

        var priority = wanted.GroupBy(w => w.IngredientId).ToDictionary(g => g.Key, g => g.Max(w => w.Priority));
        var names = wanted.GroupBy(w => w.IngredientId).ToDictionary(g => g.Key, g => g.First().Name);
        var ingredientIds = priority.Keys.ToList();
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        var candidates = await CatalogRules.Sellable(context.StoreProducts.AsNoTracking(), storeId)
            .Where(sp => context.ProductActiveIngredients.Any(l =>
                l.ProductId == sp.ProductId && ingredientIds.Contains(l.ActiveIngredientId)))
            .Select(sp => new { sp.Id, sp.ProductId, sp.Product.Name, Sku = sp.StoreSku ?? sp.Product.Sku, sp.Product.ImageUrl })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        var productIds = candidates.Select(c => c.ProductId).Distinct().ToList();
        var matched = (await context.ProductActiveIngredients.AsNoTracking()
                .Where(l => productIds.Contains(l.ProductId) && ingredientIds.Contains(l.ActiveIngredientId))
                .Select(l => new { l.ProductId, l.ActiveIngredientId })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.ActiveIngredientId).Distinct().ToList());

        return candidates
            .Select(c =>
            {
                var ingredients = matched.GetValueOrDefault(c.ProductId) ?? [];

                return new
                {
                    Candidate = c,
                    Best = ingredients.Count == 0 ? int.MinValue : ingredients.Max(i => priority[i]),
                    ingredients.Count,
                    Reason = "Chứa hoạt chất " + string.Join(", ", ingredients.Select(i => names[i]).Order(StringComparer.OrdinalIgnoreCase))
                };
            })
            .OrderByDescending(r => r.Best).ThenByDescending(r => r.Count)
            .ThenBy(r => r.Candidate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Candidate.Id)
            .Take(DiagnosisResultPolicy.MaxSuggestedProducts)
            .Select((r, index) => new RecommendedProductResponse(
                r.Candidate.Id, r.Candidate.ProductId, r.Candidate.Name, r.Candidate.Sku, r.Candidate.ImageUrl, r.Reason, index + 1))
            .ToList();
    }

    // A case decided before 2.0: the reviewer's disease and the recommendations stored on the current review, limited to
    // what can still be bought today.
    private async Task<DiagnosisResultResponse?> ReviewedAsync(Guid caseId, Disease disease, CancellationToken cancellationToken)
    {
        var review = await context.AgentReviews.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DiagnosisCaseId == caseId && r.IsCurrent, cancellationToken);
        if (review is null)
        {
            return null;
        }

        var items = await context.RecommendationItems.AsNoTracking()
            .Include(r => r.DiseaseTreatment).ThenInclude(t => t!.ActiveIngredient)
            .Include(r => r.StoreProduct).ThenInclude(p => p!.Product)
            .Where(r => r.DiagnosisCaseId == caseId && r.AgentReviewId == review.Id && r.IsActive)
            .OrderBy(r => r.RankOrder).ThenBy(r => r.ApprovedAt)
            .ToListAsync(cancellationToken);

        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var productIds = items.Where(r => r.StoreProductId != null).Select(r => r.StoreProductId!.Value).ToList();
        var sellable = (await CatalogRules.Sellable(context.StoreProducts.AsNoTracking(), storeId)
                .Where(sp => productIds.Contains(sp.Id)).Select(sp => sp.Id).ToListAsync(cancellationToken))
            .ToHashSet();

        var treatments = items
            .Where(r => r is { RecommendationType: RecommendationType.Treatment, DiseaseTreatment.IsActive: true })
            .Select(r => new TreatmentResponse(
                r.DiseaseTreatment!.Id,
                EnumText.Format(r.DiseaseTreatment.TreatmentType),
                r.DiseaseTreatment.Title,
                r.DiseaseTreatment.Instructions,
                r.DiseaseTreatment.Precautions,
                r.RankOrder,
                r.Reason,
                r.DiseaseTreatment.ActiveIngredient?.Name))
            .ToList();

        var products = items
            .Where(r => r.RecommendationType == RecommendationType.Product && r.StoreProduct is not null
                && sellable.Contains(r.StoreProduct.Id))
            .Select(r => new RecommendedProductResponse(
                r.StoreProduct!.Id,
                r.StoreProduct.ProductId,
                r.StoreProduct.Product.Name,
                r.StoreProduct.StoreSku ?? r.StoreProduct.Product.Sku,
                r.StoreProduct.Product.ImageUrl,
                r.Reason,
                r.RankOrder))
            .ToList();

        return new DiagnosisResultResponse(
            Summary(disease), review.Comment, treatments, products, EnumText.Format(DiagnosisCertainty.Verified), null);
    }

    public static DiseaseSummary Summary(Disease disease) =>
        new(disease.Id, disease.Code, disease.Name, disease.IsHealthyClass, disease.Symptoms, disease.Prevention);
}
