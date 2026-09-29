using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 60: diagnosis_images. At most one active primary image per case.
internal sealed class DiagnosisImageConfiguration : EntityConfiguration<DiagnosisImage>
{
    protected override string TableName => "diagnosis_images";

    protected override void ConfigureEntity(EntityTypeBuilder<DiagnosisImage> builder)
    {
        builder.Property(image => image.StorageKey).HasMaxLength(1000);
        builder.Property(image => image.ImageUrl).HasMaxLength(1500);
        builder.Property(image => image.FileName).HasMaxLength(255);
        builder.Property(image => image.MimeType).HasMaxLength(100);
        builder.Property(image => image.IsPrimary).HasDbDefault(false);

        builder.HasIndex(image => new { image.DiagnosisCaseId, image.UploadedAt });
        builder.HasIndex(image => image.DiagnosisCaseId, "ux_diagnosis_images_primary")
            .IsUnique()
            .HasFilter("is_primary = TRUE AND deleted_at IS NULL")
            .HasDatabaseName("ux_diagnosis_images_primary");
    }
}
