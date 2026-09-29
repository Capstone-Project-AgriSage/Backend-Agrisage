using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 63: recommendation_items. Exactly one target per recommendation type.
internal sealed class RecommendationItemConfiguration : EntityConfiguration<RecommendationItem>
{
    protected override string TableName => "recommendation_items";

    protected override void ConfigureEntity(EntityTypeBuilder<RecommendationItem> builder)
    {
        builder.Property(item => item.RecommendationType).HasMaxLength(20);
        builder.Property(item => item.RankOrder).HasDbDefault(0);
        builder.Property(item => item.Reason).HasMaxLength(1000);
        builder.Property(item => item.IsActive).HasDbDefault(true);

        builder.HasReference<RecommendationItem, AgentReview>(item => item.AgentReviewId);
        builder.HasOne(item => item.DiseaseTreatment).WithMany().HasForeignKey(item => item.DiseaseTreatmentId);
        builder.HasOne(item => item.StoreProduct).WithMany().HasForeignKey(item => item.StoreProductId);
        builder.HasUserReference(item => item.ApprovedBy);

        builder.HasCheck(
            "single_target",
            "(recommendation_type = 'TREATMENT' AND disease_treatment_id IS NOT NULL AND store_product_id IS NULL) "
            + "OR (recommendation_type = 'PRODUCT' AND store_product_id IS NOT NULL AND disease_treatment_id IS NULL)");
    }
}
