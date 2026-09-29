using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Farmer diagnosis case; aggregate root for Images, AI Inferences, Human Reviews and Recommendations
// (database design §35.12). AI inference is evidence only; a VERIFIED diagnosis comes from Human Review.
// Calling the AI service, reviewer authorization (can_review_ai) and product search are Application concerns.
public sealed class DiagnosisCase : SoftDeletableEntity
{
    private readonly List<DiagnosisImage> _images = [];
    private readonly List<AiInference> _inferences = [];
    private readonly List<AgentReview> _reviews = [];
    private readonly List<RecommendationItem> _recommendations = [];

    private DiagnosisCase()
    {
    }

    public DiagnosisCase(
        Guid farmerProfileId,
        string caseNumber,
        DateTimeOffset submittedAt,
        string? farmerNote = null,
        string cropType = Disease.RiceCropType)
    {
        FarmerProfileId = farmerProfileId;
        CaseNumber = Guard.NotNullOrWhiteSpace(caseNumber);
        CropType = Guard.NotNullOrWhiteSpace(cropType);
        SubmittedAt = submittedAt;
        FarmerNote = farmerNote;
        Status = DiagnosisCaseStatus.Submitted;
    }

    public Guid FarmerProfileId { get; private set; }

    public FarmerProfile FarmerProfile { get; private set; } = null!;

    public string CaseNumber { get; private set; } = null!;

    public string CropType { get; private set; } = null!;

    public DiagnosisCaseStatus Status { get; private set; }

    public Guid? FinalDiseaseId { get; private set; }

    public Disease? FinalDisease { get; private set; }

    public string? FarmerNote { get; private set; }

    public string? ReviewSummary { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyCollection<DiagnosisImage> Images => _images.AsReadOnly();

    public IReadOnlyCollection<AiInference> Inferences => _inferences.AsReadOnly();

    public IReadOnlyCollection<AgentReview> Reviews => _reviews.AsReadOnly();

    public IReadOnlyCollection<RecommendationItem> Recommendations => _recommendations.AsReadOnly();

    public AgentReview? CurrentReview => ActiveReviews.SingleOrDefault(r => r.IsCurrent);

    public DiagnosisImage AddImage(
        string storageKey,
        string imageUrl,
        DateTimeOffset uploadedAt,
        bool isPrimary = false,
        string? fileName = null,
        string? mimeType = null,
        long? fileSizeBytes = null,
        int? width = null,
        int? height = null)
    {
        EnsureImagesEditable();

        var image = new DiagnosisImage(Id, storageKey, imageUrl, uploadedAt, fileName, mimeType, fileSizeBytes, width, height);
        _images.Add(image);

        if (isPrimary)
        {
            MakePrimary(image);
        }

        return image;
    }

    // At most one active primary image per case.
    public void SetPrimaryImage(Guid imageId)
    {
        EnsureImagesEditable();
        MakePrimary(GetImage(imageId));
    }

    public void RemoveImage(Guid imageId, Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureImagesEditable();
        GetImage(imageId).RemoveFromAggregate(deletedBy, deletedAt);
    }

    // At least one active image is required before AI inference; a FAILED case may be processed again.
    public void StartProcessing()
    {
        EnsureStatus(DiagnosisCaseStatus.Submitted, DiagnosisCaseStatus.Failed);

        if (!ActiveImages.Any())
        {
            throw new DomainException($"Diagnosis case '{CaseNumber}' has no image to analyse.");
        }

        Status = DiagnosisCaseStatus.Processing;
    }

    // Records the exact ACTIVE Model and the ACTIVE Policy of that model effective at inference time.
    public AiInference RecordInference(
        Guid diagnosisImageId,
        AiModel aiModel,
        AiPolicyConfig aiPolicyConfig,
        string predictedClassLabel,
        decimal confidence,
        bool passedPolicy,
        AiInferenceStatus status,
        DateTimeOffset inferredAt,
        Guid? predictedDiseaseId = null,
        string? topPredictions = null,
        string? rawOutput = null,
        int? inferenceDurationMs = null)
    {
        EnsureStatus(DiagnosisCaseStatus.Processing);
        GetImage(diagnosisImageId);

        if (aiModel.Status != AiModelStatus.Active)
        {
            throw new DomainException($"AI model '{aiModel.Name} {aiModel.Version}' is not active.");
        }

        if (aiPolicyConfig.AiModelId != aiModel.Id
            || aiPolicyConfig.Status != AiPolicyConfigStatus.Active
            || !aiPolicyConfig.IsEffectiveAt(inferredAt))
        {
            throw new DomainException("The AI policy must be the active policy of this model, effective at inference time.");
        }

        if (confidence is < 0 or > 1 || confidence != CostRounding.RoundUnitCost(confidence))
        {
            throw new DomainException("Confidence must be between 0 and 1 with at most 6 decimal places.");
        }

        if (passedPolicy && (status != AiInferenceStatus.Success || confidence < aiPolicyConfig.MinimumConfidence))
        {
            throw new DomainException("An inference passes the policy only when it succeeded with at least the minimum confidence.");
        }

        if (inferenceDurationMs is < 0)
        {
            throw new DomainException("Inference duration must not be negative.");
        }

        var inference = new AiInference(
            Id,
            diagnosisImageId,
            aiModel.Id,
            aiPolicyConfig.Id,
            predictedClassLabel,
            confidence,
            passedPolicy,
            status,
            inferredAt,
            predictedDiseaseId,
            topPredictions,
            rawOutput,
            inferenceDurationMs);

        _inferences.Add(inference);

        return inference;
    }

    public void CompleteAi()
    {
        EnsureStatus(DiagnosisCaseStatus.Processing);

        if (!SuccessfulInferences.Any())
        {
            throw new DomainException($"Diagnosis case '{CaseNumber}' has no successful inference.");
        }

        Status = DiagnosisCaseStatus.AiCompleted;
    }

    public void FailAi()
    {
        EnsureStatus(DiagnosisCaseStatus.Processing);
        Status = DiagnosisCaseStatus.Failed;
    }

    public void StartReview()
    {
        EnsureStatus(DiagnosisCaseStatus.AiCompleted);
        Status = DiagnosisCaseStatus.UnderReview;
    }

    // Human Review after AI (or after AI failed, as a manual diagnosis), or a re-review that supersedes
    // the current one and deactivates its recommendations.
    public AgentReview Review(
        Guid reviewerMemberId,
        AgentReviewDecision decision,
        DateTimeOffset reviewedAt,
        Disease? finalDisease = null,
        Guid? primaryAiInferenceId = null,
        string? comment = null,
        string? caseReviewSummary = null)
    {
        EnsureStatus(
            DiagnosisCaseStatus.AiCompleted,
            DiagnosisCaseStatus.UnderReview,
            DiagnosisCaseStatus.Failed,
            DiagnosisCaseStatus.Verified,
            DiagnosisCaseStatus.Inconclusive);

        if (decision == AgentReviewDecision.Inconclusive)
        {
            if (finalDisease is not null)
            {
                throw new DomainException("An INCONCLUSIVE review has no final disease.");
            }
        }
        else if (finalDisease is null || !finalDisease.IsActive || finalDisease.IsDeleted)
        {
            throw new DomainException($"A {decision} review requires an active final disease.");
        }

        AiInference? primaryInference = null;

        if (primaryAiInferenceId is not null)
        {
            primaryInference = SuccessfulInferences.SingleOrDefault(i => i.Id == primaryAiInferenceId)
                ?? throw new DomainException("The primary inference must be a successful inference of this case.");
        }

        if (decision == AgentReviewDecision.Confirmed)
        {
            if (!SuccessfulInferences.Any())
            {
                throw new DomainException("A case without a successful AI inference cannot be CONFIRMED; use CORRECTED or INCONCLUSIVE.");
            }

            if (primaryInference is not null && primaryInference.PredictedDiseaseId != finalDisease!.Id)
            {
                throw new DomainException("CONFIRMED must keep the primary inference's predicted disease; use CORRECTED.");
            }
        }

        var review = new AgentReview(
            Id,
            reviewerMemberId,
            decision,
            reviewedAt,
            finalDisease?.Id,
            primaryAiInferenceId,
            primaryInference?.PredictedDiseaseId,
            comment);

        var previous = CurrentReview;

        if (previous is not null)
        {
            previous.Supersede(review.Id, reviewedAt);

            foreach (var recommendation in ActiveRecommendations.Where(r => r.AgentReviewId == previous.Id && r.IsActive))
            {
                recommendation.Deactivate();
            }
        }

        _reviews.Add(review);

        Status = review.IsVerified ? DiagnosisCaseStatus.Verified : DiagnosisCaseStatus.Inconclusive;
        FinalDiseaseId = finalDisease?.Id;
        CompletedAt = reviewedAt;
        ReviewSummary = caseReviewSummary ?? ReviewSummary;

        return review;
    }

    // Only a VERIFIED case gets recommendations, attached to its current review. A Healthy result gets
    // treatment guidance only. Choosing suitable products (ingredients, stock) is done by Application.
    public RecommendationItem AddRecommendation(
        RecommendationType recommendationType,
        Disease finalDisease,
        Guid approvedBy,
        DateTimeOffset approvedAt,
        DiseaseTreatment? diseaseTreatment = null,
        StoreProduct? storeProduct = null,
        int rankOrder = 0,
        string? reason = null)
    {
        EnsureStatus(DiagnosisCaseStatus.Verified);

        var review = CurrentReview;

        if (review is null || !review.IsVerified)
        {
            throw new DomainException($"Diagnosis case '{CaseNumber}' has no current verified review.");
        }

        if (finalDisease.Id != FinalDiseaseId)
        {
            throw new DomainException("The disease is not this case's verified final disease.");
        }

        if (recommendationType == RecommendationType.Treatment)
        {
            if (diseaseTreatment is null || storeProduct is not null)
            {
                throw new DomainException("A TREATMENT recommendation targets exactly one disease treatment.");
            }

            if (diseaseTreatment.DiseaseId != FinalDiseaseId || !diseaseTreatment.IsActive || diseaseTreatment.IsDeleted)
            {
                throw new DomainException("The treatment must be an active treatment of the verified disease.");
            }
        }
        else
        {
            if (storeProduct is null || diseaseTreatment is not null)
            {
                throw new DomainException("A PRODUCT recommendation targets exactly one store product.");
            }

            if (finalDisease.IsHealthyClass)
            {
                throw new DomainException("A Healthy result receives treatment guidance only, not product recommendations.");
            }

            if (!storeProduct.IsActive || !storeProduct.IsSellable || storeProduct.IsDeleted)
            {
                throw new DomainException("The recommended store product must be active and sellable.");
            }
        }

        var recommendation = new RecommendationItem(
            Id,
            review.Id,
            recommendationType,
            diseaseTreatment?.Id,
            storeProduct?.Id,
            rankOrder,
            reason,
            approvedBy,
            approvedAt);

        _recommendations.Add(recommendation);

        return recommendation;
    }

    public void DeactivateRecommendation(Guid recommendationId)
    {
        var recommendation = ActiveRecommendations.SingleOrDefault(r => r.Id == recommendationId)
            ?? throw new DomainException($"Recommendation '{recommendationId}' was not found.");

        recommendation.Deactivate();
    }

    // Possible from any state before a review completes.
    public void Cancel()
    {
        EnsureStatus(
            DiagnosisCaseStatus.Submitted,
            DiagnosisCaseStatus.Processing,
            DiagnosisCaseStatus.AiCompleted,
            DiagnosisCaseStatus.UnderReview,
            DiagnosisCaseStatus.Failed);

        Status = DiagnosisCaseStatus.Cancelled;
    }

    private IEnumerable<DiagnosisImage> ActiveImages => _images.Where(i => !i.IsDeleted);

    private IEnumerable<AiInference> SuccessfulInferences =>
        _inferences.Where(i => !i.IsDeleted && i.Status == AiInferenceStatus.Success);

    private IEnumerable<AgentReview> ActiveReviews => _reviews.Where(r => !r.IsDeleted);

    private IEnumerable<RecommendationItem> ActiveRecommendations => _recommendations.Where(r => !r.IsDeleted);

    private DiagnosisImage GetImage(Guid imageId) =>
        ActiveImages.SingleOrDefault(i => i.Id == imageId)
        ?? throw new DomainException($"Image '{imageId}' was not found on diagnosis case '{CaseNumber}'.");

    private void MakePrimary(DiagnosisImage image)
    {
        foreach (var other in ActiveImages.Where(i => i.IsPrimary && i.Id != image.Id))
        {
            other.SetPrimary(false);
        }

        image.SetPrimary(true);
    }

    private void EnsureImagesEditable() =>
        EnsureStatus(DiagnosisCaseStatus.Submitted, DiagnosisCaseStatus.Failed);

    private void EnsureStatus(params DiagnosisCaseStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException($"Diagnosis case '{CaseNumber}' is {Status}; this action is not allowed.");
        }
    }
}
