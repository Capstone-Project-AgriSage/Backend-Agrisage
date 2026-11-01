using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.UnitTests.Domain.Features.Advisory;

public class DiseaseProductTests
{
    private static readonly Guid Disease = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

    private static DiseaseProduct Protection() => new(
        Disease, Product, RecommendationGroup.Protection, "Pha theo nhãn", registrationSource: "TT 75/2025/TT-BNNMT, Phụ lục I, tr. 205",
        dosePerHaBase: 0.5m, applications: 2, preHarvestDays: 14);

    [Fact]
    public void A_new_link_is_a_draft()
    {
        var link = Protection();

        Assert.Equal(DiseaseProductStatus.Draft, link.Status);
        Assert.Null(link.ApprovedBy);
    }

    [Fact]
    public void A_protection_link_needs_its_registration_source()
    {
        Assert.Throws<DomainException>(() => new DiseaseProduct(Disease, Product, RecommendationGroup.Protection, "Pha theo nhãn"));
        Assert.Throws<DomainException>(() => new DiseaseProduct(
            Disease, Product, RecommendationGroup.Protection, "Pha theo nhãn", registrationSource: "  "));
    }

    [Fact]
    public void A_nutrition_link_needs_its_reason_but_no_registration()
    {
        Assert.Throws<DomainException>(() => new DiseaseProduct(Disease, Product, RecommendationGroup.Nutrition, "Bón 100 kg/ha"));

        var link = new DiseaseProduct(Disease, Product, RecommendationGroup.Nutrition, "Bón 100 kg/ha", reason: "Bổ sung kali");
        Assert.Null(link.RegistrationSource);
    }

    [Theory]
    [InlineData(0, 1, null)]
    [InlineData(-1, 1, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 11, null)]
    [InlineData(1, 1, -1)]
    public void Dose_applications_and_interval_are_checked(double dose, int applications, int? preHarvestDays)
    {
        Assert.Throws<DomainException>(() => new DiseaseProduct(
            Disease, Product, RecommendationGroup.Protection, "Pha theo nhãn", registrationSource: "TT 75/2025",
            dosePerHaBase: (decimal)dose, applications: applications, preHarvestDays: preHarvestDays));
    }

    [Fact]
    public void Approval_records_who_and_when()
    {
        var link = Protection();

        link.Approve(Approver, Now);

        Assert.Equal(DiseaseProductStatus.Approved, link.Status);
        Assert.Equal(Approver, link.ApprovedBy);
        Assert.Equal(Now, link.ApprovedAt);
        Assert.Throws<DomainException>(() => link.Approve(Approver, Now));
        Assert.Throws<DomainException>(() => Protection().Approve(Guid.Empty, Now));
    }

    [Fact]
    public void Changing_an_approved_link_sends_it_back_to_review()
    {
        var link = Protection();
        link.Approve(Approver, Now);

        link.Update(RecommendationGroup.Protection, "Pha liều khác", "TT 75/2025", null, null, 0.7m, 2, 14, null, 0);

        Assert.Equal(DiseaseProductStatus.Draft, link.Status);
        Assert.Null(link.ApprovedBy);
        Assert.Null(link.ApprovedAt);
        Assert.Equal("Pha liều khác", link.DoseText);
    }

    [Fact]
    public void A_rejected_change_leaves_an_approved_link_approved()
    {
        var link = Protection();
        link.Approve(Approver, Now);

        Assert.Throws<DomainException>(() => link.Update(RecommendationGroup.Protection, " ", "TT 75/2025", null, null, 0.7m, 2, 14, null, 0));

        Assert.Equal(DiseaseProductStatus.Approved, link.Status);
    }

    [Fact]
    public void Deactivating_withdraws_the_approval_and_it_can_be_approved_again()
    {
        var link = Protection();
        link.Approve(Approver, Now);

        link.Deactivate();
        Assert.Equal(DiseaseProductStatus.Inactive, link.Status);
        Assert.Null(link.ApprovedBy);

        link.Approve(Approver, Now.AddDays(1));
        Assert.Equal(DiseaseProductStatus.Approved, link.Status);
    }

    [Fact]
    public void Growth_stages_filter_the_link()
    {
        var anyStage = Protection();
        var heading = new DiseaseProduct(
            Disease, Product, RecommendationGroup.Protection, "Pha theo nhãn", registrationSource: "TT 75/2025",
            growthStages: [GrowthStage.Heading, GrowthStage.Booting, GrowthStage.Heading]);

        Assert.True(anyStage.FitsStage(GrowthStage.Seedling));
        Assert.True(heading.FitsStage(null));
        Assert.True(heading.FitsStage(GrowthStage.Heading));
        Assert.False(heading.FitsStage(GrowthStage.Seedling));
        Assert.Equal("BOOTING,HEADING", heading.GrowthStages);
        Assert.Equal([GrowthStage.Booting, GrowthStage.Heading], heading.StageList);
    }

    [Fact]
    public void An_unknown_stage_code_is_rejected()
    {
        Assert.Throws<DomainException>(() => DiseaseProduct.ParseStages("TILLERING,FLOWERING"));
    }

    [Fact]
    public void The_five_ai_classes_are_never_deactivated_but_a_new_problem_can_be()
    {
        var seeded = new Disease("LEAF_BLAST", "Bệnh đạo ôn lá");
        Assert.Throws<DomainException>(() => seeded.Deactivate());
        Assert.True(seeded.IsActive);

        var pest = new Disease("BROWN_PLANTHOPPER", "Rầy nâu", problemType: ProblemType.Pest, searchKeywords: " rầy, rầy nâu ");
        Assert.Equal(ProblemType.Pest, pest.ProblemType);
        Assert.Equal("rầy, rầy nâu", pest.SearchKeywords);
        pest.Deactivate();
        Assert.False(pest.IsActive);
    }

    [Fact]
    public void A_session_keeps_the_area_inside_the_allowed_range()
    {
        var session = new AdvisorySession(
            Guid.NewGuid(), Guid.NewGuid(), Disease, AdvisorySource.Text, null, " dao on ", 3_000m, GrowthStage.Tillering);
        Assert.Equal("dao on", session.QueryText);

        Assert.Throws<DomainException>(() => session.Refresh(AdvisorySource.Text, null, null, 0m, null));
        Assert.Throws<DomainException>(() => session.Refresh(AdvisorySource.Text, null, null, AdvisorySession.MaxAreaSqm + 1, null));
        session.Refresh(AdvisorySource.DiagnosisCase, Guid.NewGuid(), null, null, null);
        Assert.Null(session.AreaSqm);
    }
}
