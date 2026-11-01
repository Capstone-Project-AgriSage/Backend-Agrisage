using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Domain.Features.Advisory;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Advisory;

// The Farmer's side of the advisory flow (api-flows/ADVISORY.md section 5). Identity comes from the JWT; the answer is
// computed by AdvisoryEngine and logged as an advisory session. Nothing here reserves stock or touches the cart.
public sealed class MyAdvisoryService(
    IAgriSageDbContext context,
    CurrentFarmer currentFarmer,
    AdvisoryEngine engine,
    IDateTimeProvider clock,
    AuditTrail audit) : IMyAdvisoryService
{
    // V11: a later call for the same Farmer and problem inside this window updates the latest session.
    private static readonly TimeSpan SessionReuseWindow = TimeSpan.FromMinutes(30);

    private const int SummaryLength = 140;

    public async Task<PagedResult<AdvisoryProblem>> ProblemsAsync(
        AdvisoryProblemListRequest request, CancellationToken cancellationToken)
    {
        await currentFarmer.GetAsync(cancellationToken);

        var query = context.Diseases.AsNoTracking().Where(d => d.IsActive && !d.IsHealthyClass);
        if (EnumText.TryParse<ProblemType>(request.Type, out var type))
        {
            query = query.Where(d => d.ProblemType == type);
        }

        var rows = await query
            .Select(d => new { d.Id, d.Code, d.Name, d.ProblemType, d.SearchKeywords, d.Symptoms, d.Description })
            .ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(r => r.Id);

        var hits = ProblemSearch.Rank(rows.Select(r => new ProblemSearch.Candidate(r.Id, r.Name, r.SearchKeywords, r.Symptoms)), request.Search);
        var items = hits
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(hit =>
            {
                var row = byId[hit.Candidate.Id];

                return new AdvisoryProblem(
                    row.Id, row.Code, row.Name, EnumText.Format(row.ProblemType), Summarize(row.Symptoms ?? row.Description),
                    hit.MatchedBy == ProblemSearch.Match.None ? null : EnumText.Format(hit.MatchedBy));
            })
            .ToList();

        return new PagedResult<AdvisoryProblem>(items, request.Page, request.PageSize, hits.Count);
    }

    public async Task<AdvisoryRecommendationResponse> RecommendAsync(AdvisoryRecommendationRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        var disease = await context.Diseases.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.DiseaseId && d.IsActive, cancellationToken)
            ?? throw new NotFoundException("Problem", request.DiseaseId);
        if (disease.IsHealthyClass)
        {
            throw new BusinessRuleException("A healthy leaf needs no advice.");
        }

        if (request.DiagnosisCaseId is { } caseId
            && !await context.DiagnosisCases.AsNoTracking()
                .AnyAsync(c => c.Id == caseId && c.FarmerProfileId == farmer.FarmerProfileId, cancellationToken))
        {
            throw new NotFoundException("Diagnosis case", caseId);
        }

        GrowthStage? stage = EnumText.TryParse<GrowthStage>(request.GrowthStage, out var parsed) ? parsed : null;
        var outcome = await engine.BuildAsync(disease, farmer.FarmerProfileId, request.AreaSqm, stage, cancellationToken);

        var session = await SaveSessionAsync(farmer.FarmerProfileId, storeId, disease, request, stage, outcome, cancellationToken);
        var care = await CareGuidanceAsync(disease.Id, cancellationToken);

        return new AdvisoryRecommendationResponse(
            session.Id,
            new AdvisoryProblemDetail(
                disease.Id, disease.Code, disease.Name, EnumText.Format(disease.ProblemType),
                disease.Description, disease.Symptoms, disease.Causes, disease.Prevention),
            care,
            request.AreaSqm,
            stage is null ? null : EnumText.Format(stage.Value),
            outcome.Protection.Select(e => e.Product).ToList(),
            outcome.Nutrition.Select(e => e.Product).ToList(),
            outcome.All.Any() ? null : outcome.HasApprovedLinks ? AdvisoryText.NothingInStore : AdvisoryText.NoApprovedProduct,
            AdvisoryText.SafetyNote);
    }

    private async Task<AdvisorySession> SaveSessionAsync(
        Guid farmerProfileId,
        Guid storeId,
        Domain.Features.Diagnosis.Entities.Disease disease,
        AdvisoryRecommendationRequest request,
        GrowthStage? stage,
        AdvisoryEngine.Outcome outcome,
        CancellationToken cancellationToken)
    {
        var source = request.DiagnosisCaseId is null ? AdvisorySource.Text : AdvisorySource.DiagnosisCase;
        var window = clock.UtcNow - SessionReuseWindow;
        var session = await context.AdvisorySessions
            .Where(s => s.FarmerProfileId == farmerProfileId && s.DiseaseId == disease.Id && s.CreatedAt >= window)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            session = new AdvisorySession(
                storeId, farmerProfileId, disease.Id, source, request.DiagnosisCaseId, request.QueryText, request.AreaSqm, stage);
            context.AdvisorySessions.Add(session);
        }
        else
        {
            session.Refresh(source, request.DiagnosisCaseId, request.QueryText, request.AreaSqm, stage);
            context.AdvisorySessionItems.RemoveRange(
                await context.AdvisorySessionItems.Where(i => i.AdvisorySessionId == session.Id).ToListAsync(cancellationToken));
        }

        foreach (var entry in outcome.All)
        {
            context.AdvisorySessionItems.Add(new AdvisorySessionItem(
                session.Id,
                entry.Product.StoreProductId,
                entry.DiseaseProductId,
                entry.Product.RankOrder,
                entry.Product.Plan?.BaseQuantity,
                entry.Product.Plan?.TotalAmount));
        }

        audit.Record(
            "ADVISORY_RECOMMENDED", "ADVISORY_SESSION", session.Id, storeId,
            newValues: new
            {
                DiseaseCode = disease.Code,
                Source = EnumText.Format(source),
                request.AreaSqm,
                GrowthStage = stage is null ? null : EnumText.Format(stage.Value),
                StoreProductIds = outcome.All.Select(e => e.Product.StoreProductId).ToList()
            });
        await context.SaveChangesAsync(cancellationToken);

        return session;
    }

    // Care and prevention guidance of the problem; its CHEMICAL treatments are replaced by the linked products.
    private async Task<IReadOnlyList<TreatmentResponse>> CareGuidanceAsync(Guid diseaseId, CancellationToken cancellationToken) =>
        (await context.DiseaseTreatments.AsNoTracking()
                .Where(t => t.DiseaseId == diseaseId && t.IsActive && t.TreatmentType != TreatmentType.Chemical)
                .OrderByDescending(t => t.Priority).ThenBy(t => t.Title).ThenBy(t => t.Id)
                .Select(t => new { t.Id, t.TreatmentType, t.Title, t.Instructions, t.Precautions })
                .ToListAsync(cancellationToken))
            .Select((t, index) => new TreatmentResponse(
                t.Id, EnumText.Format(t.TreatmentType), t.Title, t.Instructions, t.Precautions, index + 1, null))
            .ToList();

    private static string? Summarize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = text.Trim();

        return clean.Length <= SummaryLength ? clean : clean[..SummaryLength].TrimEnd() + "…";
    }
}
