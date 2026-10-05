using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Deliveries.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// FLOW_2 §8 (task F2.6): delivery incidents. Every resolution is recorded here; when it must move stock, the movement is
// made through L4's stock adjustment and only linked (decision D5) — this service never posts stock. A customer refusing
// goods before handover is an incident (CUSTOMER_REFUSED), not a sales return.
public sealed class DeliveryIncidentService(
    IAgriSageDbContext context,
    DeliveryAccess access,
    DeliveryProofUrls proofUrls,
    IRowLockService locks,
    IDateTimeProvider clock,
    AuditTrail audit) : IDeliveryIncidentService
{
    public async Task<DeliveryIncidentResponse> ReportAsync(Guid deliveryId, ReportIncidentRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        var evidence = proofUrls.Accept(request.EvidenceImageUrl, "evidenceImageUrl");
        EnumText.TryParse<DeliveryIncidentType>(request.IncidentType, out var type);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockDeliveryAsync(deliveryId, cancellationToken);
        var delivery = await access.Scope(actor).AsNoTracking()
                .Include(d => d.Items).ThenInclude(i => i.LotAllocations)
                .Include(d => d.Attempts)
                .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery", deliveryId);

        if (request.DeliveryAttemptId is { } attemptId && delivery.Attempts.All(a => a.Id != attemptId || a.IsDeleted))
        {
            throw new BusinessRuleException("The attempt is not part of this delivery.");
        }

        if (request.AllocationId is { } allocationId)
        {
            var allocation = delivery.Items.Where(i => !i.IsDeleted).SelectMany(i => i.LotAllocations)
                    .FirstOrDefault(a => a.Id == allocationId && !a.IsDeleted)
                ?? throw new BusinessRuleException("The lot allocation is not part of this delivery.");
            if (request.AffectedBaseQuantity > allocation.AllocatedBaseQuantity)
            {
                throw new BusinessRuleException(
                    $"The affected quantity exceeds the {allocation.AllocatedBaseQuantity} base units allocated on that lot.");
            }
        }

        var incident = new DeliveryIncident(delivery.Id, type, request.Description.Trim(), actor.UserId, clock.UtcNow,
            request.DeliveryAttemptId, request.AllocationId, request.AffectedBaseQuantity, evidence);
        context.DeliveryIncidents.Add(incident);
        audit.Record("DELIVERY_INCIDENT_REPORTED", "DELIVERY", delivery.Id, delivery.StoreId, null,
            new { incident.Id, incidentType = EnumText.Format(type), request.AffectedBaseQuantity });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(incident);
    }

    public async Task<IReadOnlyList<DeliveryIncidentResponse>> ListAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        await access.EnsureVisibleAsync(actor, deliveryId, cancellationToken);

        var incidents = await context.DeliveryIncidents.AsNoTracking()
            .Where(i => i.DeliveryId == deliveryId)
            .OrderBy(i => i.ReportedAt).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        return incidents.Select(ToResponse).ToList();
    }

    public async Task<DeliveryIncidentResponse> ResolveAsync(
        Guid deliveryId, Guid incidentId, ResolveIncidentRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);
        EnumText.TryParse<DeliveryIncidentResolutionType>(request.ResolutionType, out var resolution);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockDeliveryAsync(deliveryId, cancellationToken);
        await access.EnsureVisibleAsync(actor, deliveryId, cancellationToken);
        var incident = await context.DeliveryIncidents
                .FirstOrDefaultAsync(i => i.Id == incidentId && i.DeliveryId == deliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery incident", incidentId);

        if (request.RelatedStockMovementId is { } movementId
            && !await context.StockMovements.AsNoTracking().AnyAsync(m => m.Id == movementId && m.StoreId == actor.StoreId
                && (m.MovementType == StockMovementType.AdjustmentIn || m.MovementType == StockMovementType.AdjustmentOut)
                && m.Status == StockMovementStatus.Posted, cancellationToken))
        {
            throw new BusinessRuleException(
                "relatedStockMovementId must be a posted stock adjustment of this store (POST /api/inventory/adjustments).");
        }

        incident.Resolve(resolution, actor.UserId, clock.UtcNow, Texts.Clean(request.ResolutionNote), request.RelatedStockMovementId);
        audit.Record("DELIVERY_INCIDENT_RESOLVED", "DELIVERY", deliveryId, actor.StoreId, null,
            new { incident.Id, resolutionType = EnumText.Format(resolution), request.RelatedStockMovementId });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(incident);
    }

    private static DeliveryIncidentResponse ToResponse(DeliveryIncident i) => new(
        i.Id, i.DeliveryId, i.DeliveryAttemptId, i.DeliveryItemLotAllocationId, EnumText.Format(i.IncidentType),
        i.AffectedBaseQuantity, i.Description, EnumText.Format(i.Status),
        i.ResolutionType is { } resolution ? EnumText.Format(resolution) : null, i.ResolutionNote, i.EvidenceImageUrl,
        i.RelatedStockMovementId, i.ReportedBy, i.ReportedAt, i.ResolvedBy, i.ResolvedAt);
}
