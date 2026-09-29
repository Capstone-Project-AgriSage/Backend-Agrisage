using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 61: ai_inferences. Immutable evidence with the exact Model and Policy used.
internal sealed class AiInferenceConfiguration : EntityConfiguration<AiInference>
{
    protected override string TableName => "ai_inferences";

    protected override void ConfigureEntity(EntityTypeBuilder<AiInference> builder)
    {
        builder.Property(inference => inference.PredictedClassLabel).HasMaxLength(100);
        builder.Property(inference => inference.Confidence).HasPrecision(7, 6);
        builder.Property(inference => inference.TopPredictions).IsJson();
        builder.Property(inference => inference.RawOutput).IsJson();
        builder.Property(inference => inference.Status).HasMaxLength(20);

        builder.HasReference<AiInference, DiagnosisImage>(inference => inference.DiagnosisImageId);
        builder.HasOne(inference => inference.AiModel).WithMany().HasForeignKey(inference => inference.AiModelId);
        builder.HasOne(inference => inference.AiPolicyConfig).WithMany().HasForeignKey(inference => inference.AiPolicyConfigId);
        builder.HasOne(inference => inference.PredictedDisease).WithMany().HasForeignKey(inference => inference.PredictedDiseaseId);

        builder.HasIndex(inference => new { inference.DiagnosisCaseId, inference.InferredAt }).IsDescending(false, true);

        builder.HasCheck("confidence", "confidence BETWEEN 0 AND 1");
        builder.HasCheck("inference_duration_ms", "inference_duration_ms IS NULL OR inference_duration_ms >= 0");
    }
}
