namespace AgriSage.Domain.Features.Diagnosis.Enums;

public enum DiagnosisCaseStatus
{
    Submitted,
    Processing,
    AiCompleted,
    UnderReview,
    Verified,
    Inconclusive,
    Failed,
    Cancelled
}
