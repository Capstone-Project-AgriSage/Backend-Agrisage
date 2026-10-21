using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.UnitTests.Domain.Features.Diagnosis;

// What the Farmer reads without Human Review (AI_DIAGNOSIS.md 2.0, D2, D3, D14, D15).
public class DiagnosisResultPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DiagnosisCaseStatus.AiCompleted, true, DiagnosisCertainty.Confident)]
    [InlineData(DiagnosisCaseStatus.AiCompleted, false, DiagnosisCertainty.Uncertain)]
    [InlineData(DiagnosisCaseStatus.UnderReview, true, DiagnosisCertainty.Confident)]
    [InlineData(DiagnosisCaseStatus.UnderReview, false, DiagnosisCertainty.Uncertain)]
    [InlineData(DiagnosisCaseStatus.Verified, false, DiagnosisCertainty.Verified)]
    public void A_case_with_an_answer_has_a_certainty(DiagnosisCaseStatus status, bool passed, DiagnosisCertainty expected) =>
        Assert.Equal(expected, DiagnosisResultPolicy.CertaintyOf(status, passed));

    [Theory]
    [InlineData(DiagnosisCaseStatus.Submitted)]
    [InlineData(DiagnosisCaseStatus.Processing)]
    [InlineData(DiagnosisCaseStatus.Failed)]
    [InlineData(DiagnosisCaseStatus.Cancelled)]
    [InlineData(DiagnosisCaseStatus.Inconclusive)]
    public void Nothing_is_shown_while_running_after_a_failure_a_cancel_or_an_inconclusive_review(DiagnosisCaseStatus status) =>
        Assert.Null(DiagnosisResultPolicy.CertaintyOf(status, true));

    [Fact]
    public void A_completed_case_without_a_successful_inference_shows_nothing() =>
        Assert.Null(DiagnosisResultPolicy.CertaintyOf(DiagnosisCaseStatus.AiCompleted, null));

    [Fact]
    public void Products_only_for_a_confident_disease()
    {
        Assert.True(DiagnosisResultPolicy.OffersProducts(DiagnosisCertainty.Confident, isHealthyClass: false));
        Assert.False(DiagnosisResultPolicy.OffersProducts(DiagnosisCertainty.Confident, isHealthyClass: true));
        Assert.False(DiagnosisResultPolicy.OffersProducts(DiagnosisCertainty.Uncertain, isHealthyClass: false));
        // A case reviewed before 2.0 keeps its stored recommendations; nothing new is suggested for it.
        Assert.False(DiagnosisResultPolicy.OffersProducts(DiagnosisCertainty.Verified, isHealthyClass: false));
    }

    [Theory]
    [InlineData(TreatmentType.Chemical, false)]
    [InlineData(TreatmentType.Cultural, true)]
    [InlineData(TreatmentType.Preventive, true)]
    [InlineData(TreatmentType.Other, true)]
    public void An_uncertain_guess_never_comes_with_a_pesticide(TreatmentType type, bool shown)
    {
        Assert.Equal(shown, DiagnosisResultPolicy.ShowsTreatment(DiagnosisCertainty.Uncertain, type));
        Assert.True(DiagnosisResultPolicy.ShowsTreatment(DiagnosisCertainty.Confident, type));
    }

    [Fact]
    public void A_failed_case_or_one_stuck_in_submitted_can_be_retried()
    {
        Assert.True(DiagnosisResultPolicy.CanRetry(DiagnosisCaseStatus.Failed, Now, Now));
        Assert.False(DiagnosisResultPolicy.CanRetry(DiagnosisCaseStatus.Submitted, Now, Now.AddSeconds(30)));
        Assert.True(DiagnosisResultPolicy.CanRetry(DiagnosisCaseStatus.Submitted, Now, Now.Add(DiagnosisResultPolicy.StuckSubmittedAfter)));
        Assert.False(DiagnosisResultPolicy.CanRetry(DiagnosisCaseStatus.AiCompleted, Now, Now.AddHours(1)));
        Assert.False(DiagnosisResultPolicy.CanRetry(DiagnosisCaseStatus.Cancelled, Now, Now.AddHours(1)));
    }
}
