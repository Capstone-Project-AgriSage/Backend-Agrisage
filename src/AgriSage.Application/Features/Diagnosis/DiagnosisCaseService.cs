using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// Store side of diagnosis (/api/diagnosis-cases; AI_DIAGNOSIS.md 2.0, section 8): read only. Since Human Review was
// removed, staff look at the farmers' cases (photo, AI evidence, what the Farmer saw) to follow up; nothing here changes a
// case. Reviews and recommendations are shown only for cases decided before 2.0.
public sealed class DiagnosisCaseService(
    IAgriSageDbContext context,
    DiagnosisResultBuilder results,
    DiagnosisImageUrls imageUrls) : IDiagnosisCaseService
{
    public async Task<PagedResult<DiagnosisCaseListItem>> ListAsync(
        DiagnosisCaseListRequest request, CancellationToken cancellationToken)
    {
        var query = context.DiagnosisCases.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumText.TryParse<DiagnosisCaseStatus>(request.Status, out var status))
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });
            }

            query = query.Where(c => c.Status == status);
        }

        if (request.AiPassed is { } passed)
        {
            // The latest successful inference decides, as it decides what the Farmer read.
            query = query.Where(c => context.AiInferences
                .Where(i => i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success)
                .OrderByDescending(i => i.InferredAt).ThenByDescending(i => i.Id)
                .Select(i => (bool?)i.PassedPolicy)
                .FirstOrDefault() == passed);
        }

        if (request.From is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(c => c.SubmittedAt >= start);
        }

        if (request.To is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(c => c.SubmittedAt < end);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c => c.CaseNumber.ToLower().Contains(term)
                || c.FarmerProfile.User.FullName.ToLower().Contains(term)
                || (c.FarmerProfile.User.PhoneNumber != null && c.FarmerProfile.User.PhoneNumber.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(c => c.SubmittedAt).ThenByDescending(c => c.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(c => new
            {
                c.Id,
                c.CaseNumber,
                c.Status,
                c.SubmittedAt,
                c.CompletedAt,
                c.FarmerProfileId,
                FarmerName = c.FarmerProfile.User.FullName,
                FarmerPhone = c.FarmerProfile.User.PhoneNumber,
                FinalDiseaseCode = c.FinalDisease != null ? c.FinalDisease.Code : null
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var latest = (await context.AiInferences.AsNoTracking()
                .Where(i => ids.Contains(i.DiagnosisCaseId) && i.Status == AiInferenceStatus.Success)
                .Select(i => new { i.Id, i.DiagnosisCaseId, i.PredictedClassLabel, i.Confidence, i.PassedPolicy, i.InferredAt })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.DiagnosisCaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InferredAt).ThenByDescending(i => i.Id).First());

        var items = rows.Select(r =>
        {
            latest.TryGetValue(r.Id, out var inference);

            return new DiagnosisCaseListItem(
                r.Id, r.CaseNumber, EnumText.Format(r.Status), r.SubmittedAt, r.CompletedAt,
                r.FarmerProfileId, r.FarmerName, r.FarmerPhone,
                inference?.PredictedClassLabel, inference?.Confidence, inference?.PassedPolicy, r.FinalDiseaseCode);
        }).ToList();

        return new PagedResult<DiagnosisCaseListItem>(items, request.Page, request.PageSize, total);
    }

    public Task<DiagnosisCaseResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        BuildAsync(id, cancellationToken);

    private async Task<DiagnosisCaseResponse> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        var diagnosisCase = await context.DiagnosisCases.AsNoTracking()
                .Include(c => c.FarmerProfile).ThenInclude(f => f.User)
                .Include(c => c.FinalDisease)
                .Include(c => c.Images)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        var diseaseIds = (await context.Diseases.AsNoTracking().Select(d => new { d.Id, d.Code }).ToListAsync(cancellationToken))
            .ToDictionary(d => d.Code, d => d.Id);

        var inferences = await context.AiInferences.AsNoTracking()
            .Include(i => i.AiModel).Include(i => i.AiPolicyConfig)
            .Where(i => i.DiagnosisCaseId == id)
            .OrderBy(i => i.InferredAt)
            .ToListAsync(cancellationToken);

        var reviews = await context.AgentReviews.AsNoTracking()
            .Include(r => r.FinalDisease)
            .Include(r => r.ReviewerMember).ThenInclude(m => m.User)
            .Where(r => r.DiagnosisCaseId == id)
            .OrderByDescending(r => r.ReviewedAt)
            .ToListAsync(cancellationToken);

        var recommendations = await context.RecommendationItems.AsNoTracking()
            .Include(r => r.DiseaseTreatment)
            .Include(r => r.StoreProduct).ThenInclude(p => p!.Product)
            .Where(r => r.DiagnosisCaseId == id)
            .OrderBy(r => r.RankOrder).ThenBy(r => r.ApprovedAt)
            .ToListAsync(cancellationToken);

        var images = await imageUrls.ForAsync(diagnosisCase.Images, cancellationToken);
        var farmer = diagnosisCase.FarmerProfile;
        var reviewResponses = reviews.Select(ToResponse).ToList();
        var farmerResult = await results.ForCaseAsync(id, diagnosisCase.Status, diagnosisCase.FinalDisease, cancellationToken);

        return new DiagnosisCaseResponse(
            diagnosisCase.Id,
            diagnosisCase.CaseNumber,
            EnumText.Format(diagnosisCase.Status),
            diagnosisCase.SubmittedAt,
            diagnosisCase.CompletedAt,
            new FarmerSummary(farmer.Id, farmer.UserId, farmer.User.FullName, farmer.User.PhoneNumber),
            diagnosisCase.FarmerNote,
            diagnosisCase.FinalDisease is null ? null : Summary(diagnosisCase.FinalDisease),
            images,
            inferences.Select(i => ToResponse(i, diseaseIds)).ToList(),
            reviewResponses.FirstOrDefault(r => r.IsCurrent),
            reviewResponses.Where(r => !r.IsCurrent).ToList(),
            recommendations.Select(ToResponse).ToList(),
            farmerResult);
    }

    private static AiInferenceResponse ToResponse(AiInference inference, IReadOnlyDictionary<string, Guid> diseaseIds)
    {
        var all = ParseScores(inference.TopPredictions);
        var policy = inference.AiPolicyConfig;
        var shown = all.Take(policy.TopK)
            .Select(s => new ClassScoreResponse(s.ClassLabel, diseaseIds.TryGetValue(s.ClassLabel, out var d) ? d : null, s.Confidence))
            .ToList();
        decimal? margin = all.Count == 0 ? null : all[0].Confidence - (all.Count > 1 ? all[1].Confidence : 0m);

        return new AiInferenceResponse(
            inference.Id,
            inference.DiagnosisImageId,
            EnumText.Format(inference.Status),
            inference.InferredAt,
            inference.InferenceDurationMs,
            new ModelRef(inference.AiModel.Id, inference.AiModel.Name, inference.AiModel.Version),
            new PolicyRef(policy.Id, policy.Version, policy.MinimumConfidence, policy.MinimumMargin, policy.TopK),
            inference.PredictedClassLabel,
            inference.PredictedDiseaseId,
            inference.Confidence,
            margin,
            inference.PassedPolicy,
            shown,
            ParseError(inference.RawOutput));
    }

    private static List<AiClassScore> ParseScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.EnumerateArray()
                .Select(e => new AiClassScore(
                    e.GetProperty("classLabel").GetString() ?? string.Empty, e.GetProperty("confidence").GetDecimal()))
                .OrderByDescending(s => s.Confidence)
                .ToList();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    private static string? ParseError(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawOutput);

            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AgentReviewResponse ToResponse(AgentReview review) => new(
        review.Id,
        EnumText.Format(review.Decision),
        review.FinalDisease is null ? null : Summary(review.FinalDisease),
        review.AiDiseaseIdSnapshot,
        review.PrimaryAiInferenceId,
        review.Comment,
        review.IsCurrent,
        review.ReviewedAt,
        review.ReviewerMemberId,
        review.ReviewerMember.User.FullName,
        review.SupersededAt);

    private static RecommendationResponse ToResponse(RecommendationItem item) => new(
        item.Id,
        item.AgentReviewId,
        EnumText.Format(item.RecommendationType),
        item.DiseaseTreatmentId,
        item.DiseaseTreatment?.Title,
        item.StoreProductId,
        item.StoreProduct?.Product.Name,
        item.StoreProduct?.StoreSku ?? item.StoreProduct?.Product.Sku,
        item.RankOrder,
        item.Reason,
        item.IsActive,
        item.ApprovedAt);

    private static DiseaseSummary Summary(Disease disease) => DiagnosisResultBuilder.Summary(disease);
}
