using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using static AgriSage.UnitTests.Domain.Features.Diagnosis.DiagnosisTestData;

namespace AgriSage.UnitTests.Domain.Features.Diagnosis;

public class AiModelPolicyTests
{
    [Fact]
    public void Model_moves_draft_active_retired()
    {
        var model = new AiModel("rice-disease", "1.0.0", "PyTorch", "s3://models/rice-1.0.0", "[]", StaffId);
        Assert.Throws<DomainException>(() => model.Retire(Now));

        model.Activate(Now);
        Assert.Equal(AiModelStatus.Active, model.Status);
        Assert.Equal(Now, model.DeployedAt);

        model.Retire(Now.AddDays(90));
        Assert.Equal(AiModelStatus.Retired, model.Status);
        Assert.Throws<DomainException>(() => model.Activate(Now));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(0.70001)]
    public void Policy_confidence_must_be_a_4_decimal_probability(double minimumConfidence)
    {
        Assert.Throws<DomainException>(() =>
            new AiPolicyConfig(Guid.NewGuid(), "p1", (decimal)minimumConfidence, Now, StaffId));
    }

    [Fact]
    public void Policy_requires_positive_top_k_margin_probability_and_valid_period()
    {
        Assert.Throws<DomainException>(() => new AiPolicyConfig(Guid.NewGuid(), "p1", 0.7m, Now, StaffId, topK: 0));
        Assert.Throws<DomainException>(() => new AiPolicyConfig(Guid.NewGuid(), "p1", 0.7m, Now, StaffId, minimumMargin: 2m));
        Assert.Throws<DomainException>(() => new AiPolicyConfig(Guid.NewGuid(), "p1", 0.7m, Now, StaffId, effectiveTo: Now));
    }

    [Fact]
    public void Policy_effective_period_and_activation()
    {
        var policy = new AiPolicyConfig(Guid.NewGuid(), "p1", 0.7m, Now, StaffId, effectiveTo: Now.AddDays(30));

        Assert.True(policy.RequiresHumanReview);
        Assert.False(policy.IsEffectiveAt(Now.AddSeconds(-1)));
        Assert.True(policy.IsEffectiveAt(Now));
        Assert.False(policy.IsEffectiveAt(Now.AddDays(30)));

        Assert.Throws<DomainException>(policy.Deactivate);
        policy.Activate();
        Assert.Throws<DomainException>(policy.Activate);
        policy.Deactivate();
        Assert.Equal(AiPolicyConfigStatus.Inactive, policy.Status);
    }
}
