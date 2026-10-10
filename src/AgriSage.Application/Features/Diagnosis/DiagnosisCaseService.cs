using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Products.Entities;
using Microsoft.EntityFrameworkCore;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// Reviewer side of diagnosis (/api/diagnosis-cases; AI_DIAGNOSIS.md section 8). Listing and reading need only the
// permission code of the route; every decision (start review, review, rerun, recommend) also needs the member's
// can_review_ai flag (AiReviewer, D7). One save per use case; the audit row goes with the change. The Farmer's alerts come
// from the committed-notification worker (CommittedNotificationService.DiagnosisAsync), not from here: writing one too
// would give the Farmer two for every decision.
public sealed class DiagnosisCaseService(
    IAgriSageDbContext context,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AiReviewer aiReviewer,
    DiagnosisAiRunner runner,
    DiagnosisImageUrls imageUrls,
    IPrivateFileStore privateFiles,
    AuditTrail audit) : IDiagnosisCaseService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
            query = query.Where(c => context.AiInferences.Any(i =>
                i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success && i.PassedPolicy == passed));
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
        var rows = await query.OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id)
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
                .Select(i => new { i.DiagnosisCaseId, i.PredictedClassLabel, i.Confidence, i.PassedPolicy, i.InferredAt })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.DiagnosisCaseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InferredAt).First());

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

    public async Task<DiagnosisCaseResponse> StartReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        await aiReviewer.EnsureAsync(cancellationToken);
        var diagnosisCase = await context.DiagnosisCases.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        diagnosisCase.StartReview();
        await context.SaveChangesAsync(cancellationToken);

        return await BuildAsync(id, cancellationToken);
    }

    public async Task<DiagnosisCaseResponse> ReviewAsync(Guid id, ReviewRequest request, CancellationToken cancellationToken)
    {
        var reviewer = await aiReviewer.EnsureAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        if (!EnumText.TryParse<AgentReviewDecision>(request.Decision, out var decision))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["decision"] = ["Decision must be CONFIRMED, CORRECTED or INCONCLUSIVE."]
            });
        }

        var diagnosisCase = await LoadTrackedAsync(id, cancellationToken);

        Disease? finalDisease = null;
        if (request.FinalDiseaseId is { } diseaseId)
        {
            finalDisease = await context.Diseases.FirstOrDefaultAsync(d => d.Id == diseaseId, cancellationToken)
                ?? throw new NotFoundException("Disease", diseaseId);
        }

        // The AI's answer the reviewer looked at supplies the snapshot; by default the latest successful one.
        var primary = request.PrimaryAiInferenceId
            ?? diagnosisCase.Inferences
                .Where(i => !i.IsDeleted && i.Status == AiInferenceStatus.Success)
                .OrderByDescending(i => i.InferredAt)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefault();

        var before = EnumText.Format(diagnosisCase.Status);
        var now = clock.UtcNow;

        // One transaction. A re-review needs two saves: the old review stops being current first (one current review
        // per case in the database), then the new one is inserted and linked as its successor.
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        if (diagnosisCase.CurrentReview is not null)
        {
            diagnosisCase.ReleaseCurrentReview(now);
            await context.SaveChangesAsync(cancellationToken);
        }

        var review = diagnosisCase.Review(
            reviewer.MemberId, decision, now, finalDisease, primary, Texts.Clean(request.Comment));

        audit.Record(
            "DIAGNOSIS_REVIEWED", "DIAGNOSIS_CASE", diagnosisCase.Id, storeId,
            new { Status = before },
            new { Status = EnumText.Format(diagnosisCase.Status), Decision = EnumText.Format(decision), review.FinalDiseaseId },
            Texts.Clean(request.Comment));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildAsync(id, cancellationToken);
    }

    public async Task<DiagnosisCaseResponse> RerunAiAsync(Guid id, CancellationToken cancellationToken)
    {
        await aiReviewer.EnsureAsync(cancellationToken);
        var image = await context.DiagnosisCases.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Status,
                Photo = c.Images.Where(i => i.DeletedAt == null)
                    .OrderByDescending(i => i.IsPrimary)
                    .Select(i => new { i.StorageKey, i.MimeType })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", id);

        if (image.Status is not (DiagnosisCaseStatus.Submitted or DiagnosisCaseStatus.Failed))
        {
            throw new BusinessRuleException("The AI can only be run again on a case that is submitted or failed.");
        }

        var photo = image.Photo ?? throw new BusinessRuleException("The case has no photo to analyse.");
        var bytes = await privateFiles.ReadAsync(photo.StorageKey, StorageArea.DiagnosisImages, cancellationToken);
        var contentType = photo.MimeType ?? ImageRulesContentType(photo.StorageKey);

        await runner.RunAsync(id, bytes, contentType, "photo", cancellationToken, failWhenUnavailable: true);

        return await BuildAsync(id, cancellationToken);
    }

    public async Task<RecommendationResponse> RecommendAsync(
        Guid id, RecommendationRequest request, CancellationToken cancellationToken)
    {
        await aiReviewer.EnsureAsync(cancellationToken);
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        if (!EnumText.TryParse<RecommendationType>(request.Type, out var type))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["type"] = ["Type must be TREATMENT or PRODUCT."] });
        }

        var diagnosisCase = await LoadTrackedAsync(id, cancellationToken);
        var finalDiseaseId = diagnosisCase.FinalDiseaseId
            ?? throw new BusinessRuleException("Recommendations need a verified diagnosis.");
        var finalDisease = await context.Diseases.FirstAsync(d => d.Id == finalDiseaseId, cancellationToken);

        DiseaseTreatment? treatment = null;
        StoreProduct? product = null;

        if (type == RecommendationType.Treatment)
        {
            if (request.DiseaseTreatmentId is not { } treatmentId || request.StoreProductId is not null)
            {
                throw Invalid("diseaseTreatmentId", "A TREATMENT recommendation needs exactly one diseaseTreatmentId.");
            }

            treatment = await context.DiseaseTreatments.FirstOrDefaultAsync(t => t.Id == treatmentId, cancellationToken)
                ?? throw new NotFoundException("Disease treatment", treatmentId);
        }
        else
        {
            if (request.StoreProductId is not { } productId || request.DiseaseTreatmentId is not null)
            {
                throw Invalid("storeProductId", "A PRODUCT recommendation needs exactly one storeProductId.");
            }

            product = await context.StoreProducts.Include(p => p.Product)
                    .FirstOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken)
                ?? throw new NotFoundException("Store product", productId);
        }

        var current = diagnosisCase.CurrentReview;
        if (current is not null && diagnosisCase.Recommendations.Any(r =>
                !r.IsDeleted && r.IsActive && r.AgentReviewId == current.Id
                && r.DiseaseTreatmentId == treatment?.Id && r.StoreProductId == product?.Id))
        {
            throw new ConflictException("This recommendation already exists for the current review.");
        }

        var item = diagnosisCase.AddRecommendation(
            type, finalDisease, userId, clock.UtcNow, treatment, product, request.RankOrder, Texts.Clean(request.Reason));
        audit.Record(
            "DIAGNOSIS_RECOMMENDATION_ADDED", "DIAGNOSIS_CASE", diagnosisCase.Id, storeId,
            newValues: new { Type = EnumText.Format(type), item.DiseaseTreatmentId, item.StoreProductId, item.RankOrder });
        await context.SaveChangesAsync(cancellationToken);

        return new RecommendationResponse(
            item.Id, item.AgentReviewId, EnumText.Format(type), treatment?.Id, treatment?.Title, product?.Id,
            product?.Product.Name, product?.StoreSku ?? product?.Product.Sku, item.RankOrder, item.Reason, item.IsActive,
            item.ApprovedAt);
    }

    public async Task UnrecommendAsync(Guid id, Guid recommendationId, CancellationToken cancellationToken)
    {
        await aiReviewer.EnsureAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var diagnosisCase = await LoadTrackedAsync(id, cancellationToken);

        diagnosisCase.DeactivateRecommendation(recommendationId);
        audit.Record(
            "DIAGNOSIS_RECOMMENDATION_REMOVED", "DIAGNOSIS_CASE", diagnosisCase.Id, storeId,
            newValues: new { RecommendationId = recommendationId });
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<DiagnosisCase> LoadTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await context.DiagnosisCases
            .Include(c => c.Images)
            .Include(c => c.Inferences)
            .Include(c => c.Reviews)
            .Include(c => c.Recommendations)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException("Diagnosis case", id);

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
            recommendations.Select(ToResponse).ToList());
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

    private static DiseaseSummary Summary(Disease disease) =>
        new(disease.Id, disease.Code, disease.Name, disease.IsHealthyClass, disease.Symptoms, disease.Prevention);

    private static string ImageRulesContentType(string storageKey) =>
        Files.ImageRules.ContentTypeOf(storageKey) ?? "image/jpeg";

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
