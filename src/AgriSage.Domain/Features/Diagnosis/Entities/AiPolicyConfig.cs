using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Diagnosis.Enums;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Interpretation/rejection policy of one AI Model version. "No overlapping ACTIVE periods per Model" is
// enforced transactionally by Application. requires_human_review never bypasses Human Review:
// recommendations always require a VERIFIED Diagnosis Case (database design §35.12).
public sealed class AiPolicyConfig : SoftDeletableEntity
{
    private const int ProbabilityDecimals = 4;

    private AiPolicyConfig()
    {
    }

    public AiPolicyConfig(
        Guid aiModelId,
        string version,
        decimal minimumConfidence,
        DateTimeOffset effectiveFrom,
        Guid createdBy,
        int topK = 3,
        decimal? minimumMargin = null,
        bool requiresHumanReview = true,
        string? parameters = null,
        DateTimeOffset? effectiveTo = null)
    {
        Guard.ValidPeriod(effectiveFrom, effectiveTo);

        if (topK <= 0)
        {
            throw new DomainException("top_k must be greater than zero.");
        }

        AiModelId = aiModelId;
        Version = Guard.NotNullOrWhiteSpace(version);
        MinimumConfidence = EnsureProbability(minimumConfidence, "minimum_confidence");
        MinimumMargin = minimumMargin is null ? null : EnsureProbability(minimumMargin.Value, "minimum_margin");
        TopK = topK;
        RequiresHumanReview = requiresHumanReview;
        Parameters = parameters;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedBy = createdBy;
        Status = AiPolicyConfigStatus.Draft;
    }

    public Guid AiModelId { get; private set; }

    public AiModel AiModel { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public decimal MinimumConfidence { get; private set; }

    public decimal? MinimumMargin { get; private set; }

    public int TopK { get; private set; }

    public bool RequiresHumanReview { get; private set; }

    // Raw JSON (jsonb).
    public string? Parameters { get; private set; }

    public AiPolicyConfigStatus Status { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public Guid CreatedBy { get; private set; }

    public bool IsEffectiveAt(DateTimeOffset moment) =>
        EffectiveFrom <= moment && (EffectiveTo is null || moment < EffectiveTo);

    public void Activate()
    {
        if (Status == AiPolicyConfigStatus.Active)
        {
            throw new DomainException($"AI policy '{Version}' is already active.");
        }

        Status = AiPolicyConfigStatus.Active;
    }

    public void Deactivate()
    {
        if (Status != AiPolicyConfigStatus.Active)
        {
            throw new DomainException($"Only an active AI policy can be deactivated (current: {Status}).");
        }

        Status = AiPolicyConfigStatus.Inactive;
    }

    // numeric(5,4) BETWEEN 0 AND 1.
    private static decimal EnsureProbability(decimal value, string name)
    {
        if (value is < 0 or > 1 || value != Math.Round(value, ProbabilityDecimals))
        {
            throw new DomainException($"{name} must be between 0 and 1 with at most {ProbabilityDecimals} decimal places.");
        }

        return value;
    }
}
