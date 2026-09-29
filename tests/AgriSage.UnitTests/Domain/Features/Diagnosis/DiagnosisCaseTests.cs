using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Products.Entities;
using static AgriSage.UnitTests.Domain.Features.Diagnosis.DiagnosisTestData;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Diagnosis;

public class DiagnosisCaseTests
{
    private static StoreProduct SellableStoreProduct() => CreateStoreProduct(CreateProduct());

    [Fact]
    public void Processing_requires_an_image_and_images_are_locked_afterwards()
    {
        var diagnosisCase = new DiagnosisCase(Guid.NewGuid(), "DC-0001", Now);
        Assert.Throws<DomainException>(diagnosisCase.StartProcessing);

        diagnosisCase.AddImage("k1", "https://storage.example/1.jpg", Now);
        diagnosisCase.StartProcessing();

        Assert.Equal(DiagnosisCaseStatus.Processing, diagnosisCase.Status);
        Assert.Throws<DomainException>(() => diagnosisCase.AddImage("k2", "https://storage.example/2.jpg", Now));
    }

    [Fact]
    public void At_most_one_primary_image()
    {
        var diagnosisCase = new DiagnosisCase(Guid.NewGuid(), "DC-0001", Now);
        var first = diagnosisCase.AddImage("k1", "https://storage.example/1.jpg", Now, isPrimary: true);
        var second = diagnosisCase.AddImage("k2", "https://storage.example/2.jpg", Now, isPrimary: true);

        Assert.False(first.IsPrimary);
        Assert.True(second.IsPrimary);

        diagnosisCase.SetPrimaryImage(first.Id);
        Assert.Single(diagnosisCase.Images, i => i.IsPrimary);
    }

    [Fact]
    public void Inference_records_an_active_model_and_its_active_effective_policy()
    {
        var (diagnosisCase, image) = ProcessingCase();
        var model = ActiveModel();
        var policy = ActivePolicy(model);
        var draftModel = new AiModel("rice-disease", "2.0.0", "PyTorch", "s3://models/rice-2", "[]", StaffId);
        var otherModelPolicy = ActivePolicy(ActiveModel());

        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, draftModel, policy, "LEAF_BLAST", 0.9m, false, AiInferenceStatus.Success, Now));
        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, model, otherModelPolicy, "LEAF_BLAST", 0.9m, false, AiInferenceStatus.Success, Now));
        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, model, policy, "LEAF_BLAST", 0.9m, false, AiInferenceStatus.Success, Now.AddDays(-31)));

        var inference = diagnosisCase.RecordInference(
            image.Id, model, policy, "LEAF_BLAST", 0.9m, false, AiInferenceStatus.Success, Now);

        Assert.Equal(model.Id, inference.AiModelId);
        Assert.Equal(policy.Id, inference.AiPolicyConfigId);
    }

    [Fact]
    public void Passing_the_policy_requires_success_and_minimum_confidence()
    {
        var (diagnosisCase, image) = ProcessingCase();
        var model = ActiveModel();
        var policy = ActivePolicy(model);

        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, model, policy, "LEAF_BLAST", 0.69m, passedPolicy: true, AiInferenceStatus.Success, Now));
        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, model, policy, "ERROR", 0m, passedPolicy: true, AiInferenceStatus.Failed, Now));
        Assert.Throws<DomainException>(() => diagnosisCase.RecordInference(
            image.Id, model, policy, "LEAF_BLAST", 1.01m, passedPolicy: false, AiInferenceStatus.Success, Now));
    }

    [Fact]
    public void Ai_completion_requires_a_successful_inference()
    {
        var (diagnosisCase, image) = ProcessingCase();
        var model = ActiveModel();
        diagnosisCase.RecordInference(image.Id, model, ActivePolicy(model), "ERROR", 0m, false, AiInferenceStatus.Failed, Now);

        Assert.Throws<DomainException>(diagnosisCase.CompleteAi);

        diagnosisCase.FailAi();
        Assert.Equal(DiagnosisCaseStatus.Failed, diagnosisCase.Status);

        diagnosisCase.StartProcessing();
        Assert.Equal(DiagnosisCaseStatus.Processing, diagnosisCase.Status);
    }

    [Fact]
    public void Inference_is_evidence_only_until_human_review()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, _) = AiCompletedCase(leafBlast);

        Assert.Equal(DiagnosisCaseStatus.AiCompleted, diagnosisCase.Status);
        Assert.Null(diagnosisCase.FinalDiseaseId);
        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Product, leafBlast, StaffId, Now, storeProduct: SellableStoreProduct()));
    }

    [Fact]
    public void Confirmed_review_verifies_the_ai_prediction()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, inference) = AiCompletedCase(leafBlast);
        diagnosisCase.StartReview();

        var review = diagnosisCase.Review(
            ReviewerMemberId, AgentReviewDecision.Confirmed, Now, leafBlast, inference.Id, "Typical lesions");

        Assert.Equal(DiagnosisCaseStatus.Verified, diagnosisCase.Status);
        Assert.Equal(leafBlast.Id, diagnosisCase.FinalDiseaseId);
        Assert.Equal(leafBlast.Id, review.AiDiseaseIdSnapshot);
        Assert.Same(review, diagnosisCase.CurrentReview);
        Assert.Equal(Now, diagnosisCase.CompletedAt);
    }

    [Fact]
    public void Confirmed_must_match_the_primary_inference_otherwise_use_corrected()
    {
        var leafBlast = LeafBlast();
        var brownSpot = BrownSpot();
        var (diagnosisCase, inference) = AiCompletedCase(leafBlast);

        Assert.Throws<DomainException>(() => diagnosisCase.Review(
            ReviewerMemberId, AgentReviewDecision.Confirmed, Now, brownSpot, inference.Id));

        var review = diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Corrected, Now, brownSpot, inference.Id);

        Assert.Equal(brownSpot.Id, diagnosisCase.FinalDiseaseId);
        Assert.Equal(leafBlast.Id, review.AiDiseaseIdSnapshot);
    }

    [Fact]
    public void Decision_controls_the_final_disease()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, _) = AiCompletedCase(leafBlast);

        Assert.Throws<DomainException>(() => diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Confirmed, Now));
        Assert.Throws<DomainException>(() => diagnosisCase.Review(
            ReviewerMemberId, AgentReviewDecision.Inconclusive, Now, leafBlast));
        Assert.Throws<DomainException>(() => diagnosisCase.Review(
            ReviewerMemberId, AgentReviewDecision.Corrected, Now, leafBlast, primaryAiInferenceId: Guid.NewGuid()));

        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Inconclusive, Now, comment: "Image too blurry");

        Assert.Equal(DiagnosisCaseStatus.Inconclusive, diagnosisCase.Status);
        Assert.Null(diagnosisCase.FinalDiseaseId);
    }

    [Fact]
    public void Failed_case_can_be_reviewed_manually_but_not_confirmed()
    {
        var brownSpot = BrownSpot();
        var (diagnosisCase, _) = ProcessingCase();
        diagnosisCase.FailAi();

        Assert.Throws<DomainException>(() => diagnosisCase.Review(
            ReviewerMemberId, AgentReviewDecision.Confirmed, Now, brownSpot));

        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Corrected, Now, brownSpot, comment: "Manual diagnosis");

        Assert.Equal(DiagnosisCaseStatus.Verified, diagnosisCase.Status);
        Assert.Equal(brownSpot.Id, diagnosisCase.FinalDiseaseId);
    }

    [Fact]
    public void Re_review_supersedes_the_current_review_and_deactivates_its_recommendations()
    {
        var leafBlast = LeafBlast();
        var brownSpot = BrownSpot();
        var (diagnosisCase, inference) = AiCompletedCase(leafBlast);
        var first = diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Confirmed, Now, leafBlast, inference.Id);
        var recommendation = diagnosisCase.AddRecommendation(
            RecommendationType.Product, leafBlast, StaffId, Now, storeProduct: SellableStoreProduct());

        var second = diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Corrected, Now.AddHours(1), brownSpot);

        Assert.False(first.IsCurrent);
        Assert.Equal(second.Id, first.SupersededByReviewId);
        Assert.Equal(Now.AddHours(1), first.SupersededAt);
        Assert.True(second.IsCurrent);
        Assert.False(recommendation.IsActive);
        Assert.Equal(2, diagnosisCase.Reviews.Count);
        Assert.Equal(brownSpot.Id, diagnosisCase.FinalDiseaseId);
    }

    [Fact]
    public void Inconclusive_case_gets_no_recommendation()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, _) = AiCompletedCase(leafBlast);
        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Inconclusive, Now);

        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, leafBlast, StaffId, Now,
            diseaseTreatment: new DiseaseTreatment(leafBlast.Id, TreatmentType.Cultural, "Drain field", "Drain for 3 days")));
    }

    [Fact]
    public void Recommendation_targets_exactly_one_eligible_item_of_the_verified_disease()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, inference) = AiCompletedCase(leafBlast);
        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Confirmed, Now, leafBlast, inference.Id);
        var ownTreatment = new DiseaseTreatment(leafBlast.Id, TreatmentType.Chemical, "Spray", "Spray tricyclazole");
        var otherTreatment = new DiseaseTreatment(Guid.NewGuid(), TreatmentType.Chemical, "Spray", "Other disease");
        var notSellable = SellableStoreProduct();
        notSellable.MarkNotSellable();

        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, leafBlast, StaffId, Now, diseaseTreatment: otherTreatment));
        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, leafBlast, StaffId, Now, ownTreatment, SellableStoreProduct()));
        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Product, leafBlast, StaffId, Now, storeProduct: notSellable));
        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, BrownSpot(), StaffId, Now, diseaseTreatment: ownTreatment));

        var treatment = diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, leafBlast, StaffId, Now, diseaseTreatment: ownTreatment, rankOrder: 1);

        Assert.Equal(diagnosisCase.CurrentReview!.Id, treatment.AgentReviewId);
        Assert.Null(treatment.StoreProductId);
    }

    [Fact]
    public void Healthy_result_gets_treatment_guidance_only()
    {
        var healthy = Healthy();
        var (diagnosisCase, inference) = AiCompletedCase(healthy);
        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Confirmed, Now, healthy, inference.Id);

        Assert.Throws<DomainException>(() => diagnosisCase.AddRecommendation(
            RecommendationType.Product, healthy, StaffId, Now, storeProduct: SellableStoreProduct()));

        diagnosisCase.AddRecommendation(
            RecommendationType.Treatment, healthy, StaffId, Now,
            diseaseTreatment: new DiseaseTreatment(healthy.Id, TreatmentType.Preventive, "Care", "Keep water level stable"));
    }

    [Fact]
    public void Case_cannot_be_cancelled_after_review()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, _) = AiCompletedCase(leafBlast);
        diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Inconclusive, Now);

        Assert.Throws<DomainException>(diagnosisCase.Cancel);
    }

    [Fact]
    public void Inferences_and_reviews_cannot_be_deleted_directly()
    {
        var leafBlast = LeafBlast();
        var (diagnosisCase, inference) = AiCompletedCase(leafBlast);
        var review = diagnosisCase.Review(ReviewerMemberId, AgentReviewDecision.Confirmed, Now, leafBlast, inference.Id);

        Assert.Throws<DomainException>(() => inference.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => review.MarkDeleted(StaffId, Now));
    }
}
