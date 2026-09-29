using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Immutable AI inference result recorded through DiagnosisCase. It is evidence, never the verified diagnosis;
// it keeps the exact Model and Policy version used for reproducibility.
public sealed class AiInference : SoftDeletableChildEntity
{
    private AiInference()
    {
    }

    internal AiInference(
        Guid diagnosisCaseId,
        Guid diagnosisImageId,
        Guid aiModelId,
        Guid aiPolicyConfigId,
        string predictedClassLabel,
        decimal confidence,
        bool passedPolicy,
        AiInferenceStatus status,
        DateTimeOffset inferredAt,
        Guid? predictedDiseaseId,
        string? topPredictions,
        string? rawOutput,
        int? inferenceDurationMs)
    {
        DiagnosisCaseId = diagnosisCaseId;
        DiagnosisImageId = diagnosisImageId;
        AiModelId = aiModelId;
        AiPolicyConfigId = aiPolicyConfigId;
        PredictedClassLabel = Guard.NotNullOrWhiteSpace(predictedClassLabel);
        Confidence = confidence;
        PassedPolicy = passedPolicy;
        Status = status;
        InferredAt = inferredAt;
        PredictedDiseaseId = predictedDiseaseId;
        TopPredictions = topPredictions;
        RawOutput = rawOutput;
        InferenceDurationMs = inferenceDurationMs;
    }

    public Guid DiagnosisCaseId { get; private set; }

    public Guid DiagnosisImageId { get; private set; }

    public Guid AiModelId { get; private set; }

    public AiModel AiModel { get; private set; } = null!;

    public Guid AiPolicyConfigId { get; private set; }

    public AiPolicyConfig AiPolicyConfig { get; private set; } = null!;

    public Guid? PredictedDiseaseId { get; private set; }

    public Disease? PredictedDisease { get; private set; }

    public string PredictedClassLabel { get; private set; } = null!;

    public decimal Confidence { get; private set; }

    // Raw JSON (jsonb).
    public string? TopPredictions { get; private set; }

    // Raw JSON (jsonb).
    public string? RawOutput { get; private set; }

    public bool PassedPolicy { get; private set; }

    public int? InferenceDurationMs { get; private set; }

    public AiInferenceStatus Status { get; private set; }

    public DateTimeOffset InferredAt { get; private set; }
}
