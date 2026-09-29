using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 59: diagnosis_cases. Aggregate root for images, inferences, reviews and recommendations.
internal sealed class DiagnosisCaseConfiguration : EntityConfiguration<DiagnosisCase>
{
    protected override string TableName => "diagnosis_cases";

    protected override void ConfigureEntity(EntityTypeBuilder<DiagnosisCase> builder)
    {
        builder.Property(diagnosisCase => diagnosisCase.CaseNumber).HasMaxLength(50);
        builder.Property(diagnosisCase => diagnosisCase.CropType).HasMaxLength(50).HasDbDefault(Disease.RiceCropType);
        builder.Property(diagnosisCase => diagnosisCase.Status).HasMaxLength(30);
        builder.Property(diagnosisCase => diagnosisCase.FarmerNote).HasMaxLength(1000);
        builder.Property(diagnosisCase => diagnosisCase.ReviewSummary).HasMaxLength(1000);

        builder.Ignore(diagnosisCase => diagnosisCase.CurrentReview);

        builder.HasOne(diagnosisCase => diagnosisCase.FarmerProfile).WithMany().HasForeignKey(diagnosisCase => diagnosisCase.FarmerProfileId);
        builder.HasOne(diagnosisCase => diagnosisCase.FinalDisease).WithMany().HasForeignKey(diagnosisCase => diagnosisCase.FinalDiseaseId);
        builder.HasMany(diagnosisCase => diagnosisCase.Images).WithOne().HasForeignKey(image => image.DiagnosisCaseId);
        builder.HasMany(diagnosisCase => diagnosisCase.Inferences).WithOne().HasForeignKey(inference => inference.DiagnosisCaseId);
        builder.HasMany(diagnosisCase => diagnosisCase.Reviews).WithOne().HasForeignKey(review => review.DiagnosisCaseId);
        builder.HasMany(diagnosisCase => diagnosisCase.Recommendations).WithOne().HasForeignKey(item => item.DiagnosisCaseId);

        builder.HasIndex(diagnosisCase => diagnosisCase.CaseNumber).IsUnique();
        builder.HasIndex(diagnosisCase => new { diagnosisCase.FarmerProfileId, diagnosisCase.SubmittedAt }).IsDescending(false, true);
        builder.HasIndex(diagnosisCase => new { diagnosisCase.Status, diagnosisCase.SubmittedAt });

        builder.HasCheck("verified_final_disease", "status <> 'VERIFIED' OR final_disease_id IS NOT NULL");
        builder.HasCheck("inconclusive_final_disease", "status <> 'INCONCLUSIVE' OR final_disease_id IS NULL");
    }
}
