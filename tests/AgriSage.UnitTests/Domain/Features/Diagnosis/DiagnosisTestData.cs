using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.UnitTests.Domain.Features.Diagnosis;

// Shared builders: an ACTIVE model with its ACTIVE policy (minimum confidence 0.7) and Rice disease classes.
internal static class DiagnosisTestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    public static readonly Guid StaffId = Guid.NewGuid();
    public static readonly Guid ReviewerMemberId = Guid.NewGuid();

    public static Disease LeafBlast() => new("LEAF_BLAST", "Leaf Blast");

    public static Disease BrownSpot() => new("BROWN_SPOT", "Brown Spot");

    public static Disease Healthy() => new("HEALTHY", "Healthy", isHealthyClass: true);

    public static AiModel ActiveModel()
    {
        var model = new AiModel("rice-disease", "1.0.0", "PyTorch", "s3://models/rice-1.0.0", "[]", StaffId);
        model.Activate(Now.AddDays(-30));
        return model;
    }

    public static AiPolicyConfig ActivePolicy(AiModel model)
    {
        var policy = new AiPolicyConfig(model.Id, "p1", 0.7m, Now.AddDays(-30), StaffId);
        policy.Activate();
        return policy;
    }

    // SUBMITTED case with one primary image, moved to PROCESSING.
    public static (DiagnosisCase Case, DiagnosisImage Image) ProcessingCase()
    {
        var diagnosisCase = new DiagnosisCase(Guid.NewGuid(), "DC-0001", Now);
        var image = diagnosisCase.AddImage("cases/dc-0001/1.jpg", "https://storage.example/1.jpg", Now, isPrimary: true);
        diagnosisCase.StartProcessing();
        return (diagnosisCase, image);
    }

    // AI_COMPLETED case whose single successful inference predicts the given disease.
    public static (DiagnosisCase Case, AiInference Inference) AiCompletedCase(Disease predicted)
    {
        var (diagnosisCase, image) = ProcessingCase();
        var model = ActiveModel();
        var inference = diagnosisCase.RecordInference(
            image.Id, model, ActivePolicy(model), predicted.Code, 0.93m, passedPolicy: true,
            AiInferenceStatus.Success, Now, predicted.Id);
        diagnosisCase.CompleteAi();
        return (diagnosisCase, inference);
    }
}
