using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 57: ai_models.
internal sealed class AiModelConfiguration : EntityConfiguration<AiModel>
{
    protected override string TableName => "ai_models";

    protected override void ConfigureEntity(EntityTypeBuilder<AiModel> builder)
    {
        builder.Property(model => model.Name).HasMaxLength(150);
        builder.Property(model => model.Version).HasMaxLength(50);
        builder.Property(model => model.Architecture).HasMaxLength(100);
        builder.Property(model => model.Framework).HasMaxLength(50);
        builder.Property(model => model.ModelStorageUrl).HasMaxLength(1000);
        builder.Property(model => model.ClassLabels).IsJson();
        builder.Property(model => model.Metrics).IsJson();
        builder.Property(model => model.Status).HasMaxLength(30);

        builder.HasUserReference(model => model.CreatedBy);

        builder.HasIndex(model => new { model.Name, model.Version }).IsUnique();
    }
}
