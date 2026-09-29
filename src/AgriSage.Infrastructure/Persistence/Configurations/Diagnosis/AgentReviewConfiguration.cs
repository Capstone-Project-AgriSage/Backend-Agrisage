using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// Table 62: agent_reviews. Required relationship to the reviewing Store Member; the ReviewerMember
// navigation is only populated when explicitly included (no auto-include).
internal sealed class AgentReviewConfiguration : EntityConfiguration<AgentReview>
{
    protected override string TableName => "agent_reviews";

    protected override void ConfigureEntity(EntityTypeBuilder<AgentReview> builder)
    {
        builder.Property(review => review.Decision).HasMaxLength(30);
        builder.Property(review => review.Comment).HasMaxLength(2000);
        builder.Property(review => review.IsCurrent).HasDbDefault(true);

        builder.Ignore(review => review.IsVerified);

        builder.HasReference<AgentReview, AiInference>(review => review.PrimaryAiInferenceId);
        builder.HasOne(review => review.ReviewerMember).WithMany().HasForeignKey(review => review.ReviewerMemberId);
        builder.HasReference<AgentReview, Disease>(review => review.AiDiseaseIdSnapshot);
        builder.HasOne(review => review.FinalDisease).WithMany().HasForeignKey(review => review.FinalDiseaseId);
        builder.HasReference<AgentReview, AgentReview>(review => review.SupersededByReviewId);

        builder.HasIndex(review => review.DiagnosisCaseId, "ux_agent_reviews_current")
            .IsUnique()
            .HasFilter("is_current = TRUE AND deleted_at IS NULL")
            .HasDatabaseName("ux_agent_reviews_current");
        builder.HasIndex(review => new { review.DiagnosisCaseId, review.ReviewedAt }).IsDescending(false, true);
        builder.HasIndex(review => new { review.ReviewerMemberId, review.ReviewedAt }).IsDescending(false, true);

        builder.HasCheck(
            "decision_final_disease",
            "(decision IN ('CONFIRMED', 'CORRECTED') AND final_disease_id IS NOT NULL) "
            + "OR (decision = 'INCONCLUSIVE' AND final_disease_id IS NULL)");
    }
}
