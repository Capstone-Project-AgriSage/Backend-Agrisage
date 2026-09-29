using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 58: ai_policy_configs. "No overlapping ACTIVE periods per Model" is enforced by Application,
// not by a simple unique index.
internal sealed class AiPolicyConfigConfiguration : EntityConfiguration<AiPolicyConfig>
{
    protected override string TableName => "ai_policy_configs";

    protected override void ConfigureEntity(EntityTypeBuilder<AiPolicyConfig> builder)
    {
        builder.Property(policy => policy.Version).HasMaxLength(50);
        builder.Property(policy => policy.MinimumConfidence).HasPrecision(5, 4);
        builder.Property(policy => policy.MinimumMargin).HasPrecision(5, 4);
        builder.Property(policy => policy.TopK).HasDbDefault(3);
        builder.Property(policy => policy.RequiresHumanReview).HasDbDefault(true);
        builder.Property(policy => policy.Parameters).IsJson();
        builder.Property(policy => policy.Status).HasMaxLength(20);

        builder.HasOne(policy => policy.AiModel).WithMany().HasForeignKey(policy => policy.AiModelId);
        builder.HasUserReference(policy => policy.CreatedBy);

        builder.HasIndex(policy => new { policy.AiModelId, policy.Version }).IsUnique();
        builder.HasIndex(policy => new { policy.AiModelId, policy.Status, policy.EffectiveFrom })
            .IsDescending(false, false, true);

        builder.HasCheck("minimum_confidence", "minimum_confidence BETWEEN 0 AND 1");
        builder.HasCheck("minimum_margin", "minimum_margin IS NULL OR minimum_margin BETWEEN 0 AND 1");
        builder.HasCheck("top_k", "top_k > 0");
        builder.HasCheck("effective_period", "effective_to IS NULL OR effective_to > effective_from");
    }
}
