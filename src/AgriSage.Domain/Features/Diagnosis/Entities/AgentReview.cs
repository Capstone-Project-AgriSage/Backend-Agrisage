using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Stores.Entities;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Human Review at the Diagnosis Case level. Created only through DiagnosisCase; history is never overwritten:
// a newer review supersedes this one (is_current = false). Reviewer authorization (can_review_ai) is
// checked by Application.
public sealed class AgentReview : SoftDeletableChildEntity
{
    private AgentReview()
    {
    }

    internal AgentReview(
        Guid diagnosisCaseId,
        Guid reviewerMemberId,
        AgentReviewDecision decision,
        DateTimeOffset reviewedAt,
        Guid? finalDiseaseId,
        Guid? primaryAiInferenceId,
        Guid? aiDiseaseIdSnapshot,
        string? comment)
    {
        DiagnosisCaseId = diagnosisCaseId;
        ReviewerMemberId = reviewerMemberId;
        Decision = decision;
        ReviewedAt = reviewedAt;
        FinalDiseaseId = finalDiseaseId;
        PrimaryAiInferenceId = primaryAiInferenceId;
        AiDiseaseIdSnapshot = aiDiseaseIdSnapshot;
        Comment = comment;
        IsCurrent = true;
    }

    public Guid DiagnosisCaseId { get; private set; }

    public Guid? PrimaryAiInferenceId { get; private set; }

    public Guid ReviewerMemberId { get; private set; }

    public StoreMember ReviewerMember { get; private set; } = null!;

    public AgentReviewDecision Decision { get; private set; }

    public Guid? AiDiseaseIdSnapshot { get; private set; }

    public Guid? FinalDiseaseId { get; private set; }

    public Disease? FinalDisease { get; private set; }

    public string? Comment { get; private set; }

    public bool IsCurrent { get; private set; }

    public DateTimeOffset ReviewedAt { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public Guid? SupersededByReviewId { get; private set; }

    public bool IsVerified => Decision is AgentReviewDecision.Confirmed or AgentReviewDecision.Corrected;

    internal void Supersede(Guid supersededByReviewId, DateTimeOffset supersededAt)
    {
        IsCurrent = false;
        SupersededByReviewId = supersededByReviewId;
        SupersededAt = supersededAt;
    }

    // First half of a re-review that must be saved in two steps (see DiagnosisCase.ReleaseCurrentReview).
    internal void Release(DateTimeOffset releasedAt)
    {
        IsCurrent = false;
        SupersededAt = releasedAt;
    }

    internal void LinkSuccessor(Guid successorId) => SupersededByReviewId = successorId;
}
