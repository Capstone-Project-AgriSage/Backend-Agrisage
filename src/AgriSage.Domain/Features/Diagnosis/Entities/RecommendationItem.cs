using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// Treatment or product recommendation attached to the current verified Human Review.
// Created and deactivated only through DiagnosisCase; exactly one target per type.
public sealed class RecommendationItem : SoftDeletableChildEntity
{
    private RecommendationItem()
    {
    }

    internal RecommendationItem(
        Guid diagnosisCaseId,
        Guid agentReviewId,
        RecommendationType recommendationType,
        Guid? diseaseTreatmentId,
        Guid? storeProductId,
        int rankOrder,
        string? reason,
        Guid approvedBy,
        DateTimeOffset approvedAt)
    {
        DiagnosisCaseId = diagnosisCaseId;
        AgentReviewId = agentReviewId;
        RecommendationType = recommendationType;
        DiseaseTreatmentId = diseaseTreatmentId;
        StoreProductId = storeProductId;
        RankOrder = rankOrder;
        Reason = reason;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        IsActive = true;
    }

    public Guid DiagnosisCaseId { get; private set; }

    public Guid AgentReviewId { get; private set; }

    public RecommendationType RecommendationType { get; private set; }

    public Guid? DiseaseTreatmentId { get; private set; }

    public DiseaseTreatment? DiseaseTreatment { get; private set; }

    public Guid? StoreProductId { get; private set; }

    public StoreProduct? StoreProduct { get; private set; }

    public int RankOrder { get; private set; }

    public string? Reason { get; private set; }

    public Guid ApprovedBy { get; private set; }

    public DateTimeOffset ApprovedAt { get; private set; }

    public bool IsActive { get; private set; }

    internal void Deactivate() => IsActive = false;
}
