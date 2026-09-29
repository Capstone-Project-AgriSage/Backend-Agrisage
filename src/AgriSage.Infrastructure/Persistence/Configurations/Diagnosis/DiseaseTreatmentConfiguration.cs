using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 56: disease_treatments.
internal sealed class DiseaseTreatmentConfiguration : EntityConfiguration<DiseaseTreatment>
{
    protected override string TableName => "disease_treatments";

    protected override void ConfigureEntity(EntityTypeBuilder<DiseaseTreatment> builder)
    {
        builder.Property(treatment => treatment.TreatmentType).HasMaxLength(30);
        builder.Property(treatment => treatment.Title).HasMaxLength(255);
        builder.Property(treatment => treatment.Instructions).IsText();
        builder.Property(treatment => treatment.Precautions).IsText();
        builder.Property(treatment => treatment.Priority).HasDbDefault(0);
        builder.Property(treatment => treatment.IsActive).HasDbDefault(true);

        builder.HasOne(treatment => treatment.Disease).WithMany().HasForeignKey(treatment => treatment.DiseaseId);
        builder.HasOne(treatment => treatment.ActiveIngredient).WithMany().HasForeignKey(treatment => treatment.ActiveIngredientId);
    }
}
