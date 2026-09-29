using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 55: diseases.
internal sealed class DiseaseConfiguration : EntityConfiguration<Disease>
{
    protected override string TableName => "diseases";

    protected override void ConfigureEntity(EntityTypeBuilder<Disease> builder)
    {
        builder.Property(disease => disease.Code).HasMaxLength(50);
        builder.Property(disease => disease.Name).HasMaxLength(200);
        builder.Property(disease => disease.ScientificName).HasMaxLength(255);
        builder.Property(disease => disease.CropType).HasMaxLength(50).HasDbDefault(Disease.RiceCropType);
        builder.Property(disease => disease.Description).IsText();
        builder.Property(disease => disease.Symptoms).IsText();
        builder.Property(disease => disease.Causes).IsText();
        builder.Property(disease => disease.Prevention).IsText();
        builder.Property(disease => disease.IsHealthyClass).HasDbDefault(false);
        builder.Property(disease => disease.IsActive).HasDbDefault(true);

        builder.HasIndex(disease => disease.Code).IsUnique();
    }
}
