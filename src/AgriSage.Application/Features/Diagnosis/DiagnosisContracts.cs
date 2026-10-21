using System.Text.Json;
using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Diagnosis;

// ---- Shared ----

public sealed record DiseaseSummary(Guid Id, string Code, string Name, bool IsHealthy, string? Symptoms, string? Prevention);

// A photo of a case. ImageUrl is a signed URL created at read time (private bucket); it expires at ExpiresAt.
public sealed record DiagnosisImageResponse(
    Guid Id, string ImageUrl, DateTimeOffset ExpiresAt, bool IsPrimary, string? FileName, DateTimeOffset UploadedAt);

public sealed record TreatmentResponse(
    Guid Id, string Type, string Title, string Instructions, string? Precautions, int RankOrder, string? Reason,
    string? ActiveIngredientName = null);

public sealed record RecommendedProductResponse(
    Guid StoreProductId, Guid ProductId, string Name, string Sku, string? ImageUrl, string? Reason, int RankOrder);

// ---- Farmer (/api/me/diagnosis-cases) ----

public sealed record MyDiagnosisCaseListRequest : PaginationRequest
{
    public string? Status { get; init; }
}

// DiseaseCode / DiseaseName: the predicted disease of an AI result, or the final disease of a case reviewed before 2.0.
// Certainty: CONFIDENT, UNCERTAIN or VERIFIED, null while there is no result.
public sealed record MyDiagnosisCaseListItem(
    Guid Id, string CaseNumber, string Status, DateTimeOffset SubmittedAt, DateTimeOffset? CompletedAt,
    string? DiseaseCode, string? DiseaseName, string? Certainty);

// Certainty: CONFIDENT (the AI passed the policy), UNCERTAIN (it did not: Hint = RETAKE_PHOTO_OR_ASK_STORE, no chemical
// treatment, no product) or VERIFIED (decided by a reviewer before 2.0). ReviewerComment only for VERIFIED.
public sealed record DiagnosisResultResponse(
    DiseaseSummary Disease,
    string? ReviewerComment,
    IReadOnlyList<TreatmentResponse> Treatments,
    IReadOnlyList<RecommendedProductResponse> Products,
    string Certainty,
    string? Hint);

public sealed record DiagnosisInconclusiveResponse(string? Comment, string Hint);

// What the Farmer sees (AI_DIAGNOSIS.md 2.0, section 7): a disease and a certainty, never a probability, a top-k, a model
// or a policy (D8). CanRetry: the AI failed (or the case was interrupted) and may be run again by the Farmer (D14).
public sealed record MyDiagnosisCaseResponse(
    Guid Id,
    string CaseNumber,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    string? FarmerNote,
    DiagnosisImageResponse? Image,
    DiagnosisResultResponse? Result,
    DiagnosisInconclusiveResponse? Inconclusive,
    bool CanRetry);

public sealed record CancelDiagnosisRequest(string? Reason = null);

public interface IMyDiagnosisCaseService
{
    Task<MyDiagnosisCaseResponse> CreateAsync(Stream image, string? fileName, string? note, CancellationToken cancellationToken);

    Task<PagedResult<MyDiagnosisCaseListItem>> ListAsync(MyDiagnosisCaseListRequest request, CancellationToken cancellationToken);

    Task<MyDiagnosisCaseResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<MyDiagnosisCaseResponse> CancelAsync(Guid id, CancelDiagnosisRequest request, CancellationToken cancellationToken);

    // Runs the AI again on a FAILED case (or one left SUBMITTED); 503 when the AI Service cannot be reached (D14).
    Task<MyDiagnosisCaseResponse> RetryAsync(Guid id, CancellationToken cancellationToken);
}

// ---- Store, read only (/api/diagnosis-cases) ----

public sealed record DiagnosisCaseListRequest : PaginationRequest
{
    public string? Status { get; init; }

    // true: only cases whose latest successful AI result passed the policy; false: only those that did not.
    public bool? AiPassed { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    // Case number, Farmer name or phone.
    public string? Search { get; init; }
}

public sealed record DiagnosisCaseListItem(
    Guid Id,
    string CaseNumber,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    Guid FarmerProfileId,
    string FarmerName,
    string? FarmerPhone,
    string? PredictedClassLabel,
    decimal? Confidence,
    bool? PassedPolicy,
    string? FinalDiseaseCode);

public sealed record FarmerSummary(Guid FarmerProfileId, Guid UserId, string FullName, string? PhoneNumber);

public sealed record ModelRef(Guid Id, string Name, string Version);

public sealed record PolicyRef(Guid Id, string Version, decimal MinimumConfidence, decimal? MinimumMargin, int TopK);

public sealed record ClassScoreResponse(string ClassLabel, Guid? DiseaseId, decimal Confidence);

public sealed record AiInferenceResponse(
    Guid Id,
    Guid DiagnosisImageId,
    string Status,
    DateTimeOffset InferredAt,
    int? DurationMs,
    ModelRef Model,
    PolicyRef Policy,
    string PredictedClassLabel,
    Guid? PredictedDiseaseId,
    decimal Confidence,
    decimal? Margin,
    bool PassedPolicy,
    IReadOnlyList<ClassScoreResponse> TopPredictions,
    string? Error);

public sealed record AgentReviewResponse(
    Guid Id,
    string Decision,
    DiseaseSummary? FinalDisease,
    Guid? AiDiseaseId,
    Guid? PrimaryAiInferenceId,
    string? Comment,
    bool IsCurrent,
    DateTimeOffset ReviewedAt,
    Guid ReviewerMemberId,
    string ReviewerName,
    DateTimeOffset? SupersededAt);

public sealed record RecommendationResponse(
    Guid Id,
    Guid AgentReviewId,
    string Type,
    Guid? DiseaseTreatmentId,
    string? TreatmentTitle,
    Guid? StoreProductId,
    string? ProductName,
    string? ProductSku,
    int RankOrder,
    string? Reason,
    bool IsActive,
    DateTimeOffset ApprovedAt);

public sealed record DiagnosisCaseResponse(
    Guid Id,
    string CaseNumber,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt,
    FarmerSummary Farmer,
    string? FarmerNote,
    DiseaseSummary? FinalDisease,
    IReadOnlyList<DiagnosisImageResponse> Images,
    IReadOnlyList<AiInferenceResponse> Inferences,
    // Filled only for cases reviewed before 2.0.
    AgentReviewResponse? CurrentReview,
    IReadOnlyList<AgentReviewResponse> ReviewHistory,
    IReadOnlyList<RecommendationResponse> Recommendations,
    // Exactly what the Farmer reads for this case (null when there is no result).
    DiagnosisResultResponse? FarmerResult);

public interface IDiagnosisCaseService
{
    // Newest first.
    Task<PagedResult<DiagnosisCaseListItem>> ListAsync(DiagnosisCaseListRequest request, CancellationToken cancellationToken);

    Task<DiagnosisCaseResponse> GetAsync(Guid id, CancellationToken cancellationToken);
}

// ---- AI models and policies (Manage) ----

// ClassLabels must be exactly the five disease codes. Metrics / Parameters are free JSON objects.
public sealed record AiModelRequest(
    string Name,
    string Version,
    string Framework,
    string ModelStorageUrl,
    IReadOnlyList<string> ClassLabels,
    string? Architecture = null,
    int? InputWidth = null,
    int? InputHeight = null,
    JsonElement? Metrics = null);

public sealed record AiPolicyRequest(
    string Version,
    decimal MinimumConfidence,
    DateTimeOffset EffectiveFrom,
    decimal? MinimumMargin = null,
    int TopK = 3,
    DateTimeOffset? EffectiveTo = null,
    bool RequiresHumanReview = true,
    JsonElement? Parameters = null);

public sealed record AiPolicyResponse(
    Guid Id,
    Guid AiModelId,
    string Version,
    decimal MinimumConfidence,
    decimal? MinimumMargin,
    int TopK,
    bool RequiresHumanReview,
    JsonElement? Parameters,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record AiModelResponse(
    Guid Id,
    string Name,
    string Version,
    string? Architecture,
    string Framework,
    string ModelStorageUrl,
    int? InputWidth,
    int? InputHeight,
    IReadOnlyList<string> ClassLabels,
    JsonElement? Metrics,
    string Status,
    DateTimeOffset? DeployedAt,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AiPolicyResponse> Policies);

public sealed record AiModelListRequest : PaginationRequest
{
    public string? Status { get; init; }
}

public interface IAiModelService
{
    Task<PagedResult<AiModelResponse>> ListAsync(AiModelListRequest request, CancellationToken cancellationToken);

    Task<AiModelResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<AiModelResponse> CreateAsync(AiModelRequest request, CancellationToken cancellationToken);

    // The previously ACTIVE model becomes RETIRED in the same save: one model answers at a time.
    Task<AiModelResponse> ActivateAsync(Guid id, CancellationToken cancellationToken);

    Task<AiModelResponse> RetireAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiPolicyResponse>> ListPoliciesAsync(Guid modelId, CancellationToken cancellationToken);

    Task<AiPolicyResponse> CreatePolicyAsync(Guid modelId, AiPolicyRequest request, CancellationToken cancellationToken);

    Task<AiPolicyResponse> ActivatePolicyAsync(Guid policyId, CancellationToken cancellationToken);

    Task<AiPolicyResponse> DeactivatePolicyAsync(Guid policyId, CancellationToken cancellationToken);
}

// ---- Disease content and treatments (staff) ----

public sealed record DiseaseUpdateRequest(
    string Name,
    string? ScientificName = null,
    string? Description = null,
    string? Symptoms = null,
    string? Causes = null,
    string? Prevention = null);

// TreatmentType: CULTURAL, CHEMICAL, PREVENTIVE or OTHER.
public sealed record TreatmentRequest(
    string TreatmentType,
    string Title,
    string Instructions,
    Guid? ActiveIngredientId = null,
    string? Precautions = null,
    int Priority = 0);

public sealed record TreatmentItem(
    Guid Id,
    Guid DiseaseId,
    string TreatmentType,
    string Title,
    string Instructions,
    Guid? ActiveIngredientId,
    string? ActiveIngredientName,
    string? Precautions,
    int Priority,
    bool IsActive);

public sealed record DiseaseResponse(
    Guid Id,
    string Code,
    string Name,
    string? ScientificName,
    string CropType,
    string? Description,
    string? Symptoms,
    string? Causes,
    string? Prevention,
    bool IsHealthyClass,
    bool IsActive,
    IReadOnlyList<TreatmentItem> Treatments);

public interface IDiseaseService
{
    Task<IReadOnlyList<DiseaseResponse>> ListAsync(bool? isActive, CancellationToken cancellationToken);

    Task<DiseaseResponse> UpdateAsync(Guid id, DiseaseUpdateRequest request, CancellationToken cancellationToken);

    Task<TreatmentItem> CreateTreatmentAsync(Guid diseaseId, TreatmentRequest request, CancellationToken cancellationToken);

    Task<TreatmentItem> UpdateTreatmentAsync(Guid id, TreatmentRequest request, CancellationToken cancellationToken);

    Task<TreatmentItem> SetTreatmentActiveAsync(Guid id, bool active, CancellationToken cancellationToken);
}

// Application-level switches of the diagnosis flow, set by the host.
public sealed class AiDiagnosisOptions
{
    // A prediction flagged as simulated is a real result only in Development; elsewhere it is recorded as FAILED (D10).
    public bool AllowStubResults { get; set; }
}
