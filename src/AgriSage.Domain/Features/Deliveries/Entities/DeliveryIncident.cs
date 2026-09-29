using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// Delivery problem and its resolution; the lot allocation identifies the exact affected Lot.
// Any inventory-affecting resolution must be recorded as a Stock Movement by the calling use case.
public sealed class DeliveryIncident : SoftDeletableEntity
{
    private DeliveryIncident()
    {
    }

    public DeliveryIncident(
        Guid deliveryId,
        DeliveryIncidentType incidentType,
        string description,
        Guid reportedBy,
        DateTimeOffset reportedAt,
        Guid? deliveryAttemptId = null,
        Guid? deliveryItemLotAllocationId = null,
        long? affectedBaseQuantity = null,
        string? evidenceImageUrl = null)
    {
        DeliveryId = deliveryId;
        IncidentType = incidentType;
        Description = Guard.NotNullOrWhiteSpace(description);
        ReportedBy = reportedBy;
        ReportedAt = reportedAt;
        DeliveryAttemptId = deliveryAttemptId;
        DeliveryItemLotAllocationId = deliveryItemLotAllocationId;
        AffectedBaseQuantity = affectedBaseQuantity is null ? null : Guard.Positive(affectedBaseQuantity.Value);
        EvidenceImageUrl = evidenceImageUrl;
        Status = DeliveryIncidentStatus.Open;
    }

    public Guid DeliveryId { get; private set; }

    public Guid? DeliveryAttemptId { get; private set; }

    public Guid? DeliveryItemLotAllocationId { get; private set; }

    public DeliveryIncidentType IncidentType { get; private set; }

    public long? AffectedBaseQuantity { get; private set; }

    public string Description { get; private set; } = null!;

    public DeliveryIncidentStatus Status { get; private set; }

    public DeliveryIncidentResolutionType? ResolutionType { get; private set; }

    public string? ResolutionNote { get; private set; }

    public string? EvidenceImageUrl { get; private set; }

    public Guid? RelatedStockMovementId { get; private set; }

    public Guid ReportedBy { get; private set; }

    public DateTimeOffset ReportedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Resolve(
        DeliveryIncidentResolutionType resolutionType,
        Guid resolvedBy,
        DateTimeOffset resolvedAt,
        string? resolutionNote = null,
        Guid? relatedStockMovementId = null)
    {
        if (Status != DeliveryIncidentStatus.Open)
        {
            throw new DomainException("Delivery incident is already resolved.");
        }

        Status = DeliveryIncidentStatus.Resolved;
        ResolutionType = resolutionType;
        ResolvedBy = resolvedBy;
        ResolvedAt = resolvedAt;
        ResolutionNote = resolutionNote;
        RelatedStockMovementId = relatedStockMovementId;
    }
}
