using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.Domain.Features.Diagnosis;

// What the Farmer may read for a diagnosis case, now that no one reviews the AI (AI_DIAGNOSIS.md 2.0, D2, D3, D14, D15).
public static class DiagnosisResultPolicy
{
    // At most this many products are suggested for one result (D13).
    public const int MaxSuggestedProducts = 5;

    // A case left SUBMITTED this long was interrupted between its two saves (D4) and may be retried.
    public static readonly TimeSpan StuckSubmittedAfter = TimeSpan.FromMinutes(2);

    // Null when the case has no result to show: still running, failed, cancelled, or decided INCONCLUSIVE before 2.0.
    // latestPassedPolicy is the stored value of the latest SUCCESS inference, or null when there is none.
    public static DiagnosisCertainty? CertaintyOf(DiagnosisCaseStatus status, bool? latestPassedPolicy) => status switch
    {
        DiagnosisCaseStatus.Verified => DiagnosisCertainty.Verified,
        DiagnosisCaseStatus.AiCompleted or DiagnosisCaseStatus.UnderReview when latestPassedPolicy is { } passed =>
            passed ? DiagnosisCertainty.Confident : DiagnosisCertainty.Uncertain,
        _ => null
    };

    // Products only when the AI is confident about a disease; a Healthy leaf gets care tips only.
    public static bool OffersProducts(DiagnosisCertainty certainty, bool isHealthyClass) =>
        certainty == DiagnosisCertainty.Confident && !isHealthyClass;

    // An uncertain guess never comes with a pesticide.
    public static bool ShowsTreatment(DiagnosisCertainty certainty, TreatmentType type) =>
        certainty != DiagnosisCertainty.Uncertain || type != TreatmentType.Chemical;

    public static bool CanRetry(DiagnosisCaseStatus status, DateTimeOffset submittedAt, DateTimeOffset now) =>
        status == DiagnosisCaseStatus.Failed
        || (status == DiagnosisCaseStatus.Submitted && now - submittedAt >= StuckSubmittedAfter);
}
