using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Orders;

namespace AgriSage.Application.Features.Deliveries;

// FLOW_2 §7–9 (tasks F2.5–F2.7): delivery notes, attempts, incidents, Farmer tracking and the delivery report.
// Quantities: plannedQuantity = packaging units; every …BaseQuantity = inventory base units.

public sealed record DeliveryItemRequest(Guid OrderItemId, long PlannedQuantity);

// DeliveryAddress null = snapshot of the order's address.
public sealed record CreateDeliveryRequest(
    Guid OrderId,
    IReadOnlyList<DeliveryItemRequest> Items,
    DeliveryAddressRequest? DeliveryAddress = null,
    DateTimeOffset? ScheduledAt = null,
    string? Note = null);

public sealed record AssignDeliveryRequest(Guid AssignedToUserId);

public sealed record LotQuantityRequest(Guid InventoryLotId, long BaseQuantity);

public sealed record ChangeLotsRequest(IReadOnlyList<LotQuantityRequest> Lots);

public sealed record CancelDeliveryRequest(string Reason);

// Status: enum text. FromDate/ToDate: Vietnam days on scheduledAt, or createdAt when not scheduled.
// Search: delivery number, order number or recipient name/phone.
public sealed record DeliveryListRequest : PaginationRequest
{
    public string? Status { get; init; }

    public Guid? OrderId { get; init; }

    public Guid? AssignedToUserId { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? Search { get; init; }
}

public sealed record DeliveryStaffReference(Guid UserId, string FullName, string? PhoneNumber);

public sealed record DeliveryListItem(
    Guid Id,
    string DeliveryNumber,
    Guid OrderId,
    string OrderNumber,
    string Status,
    DeliveryStaffReference? AssignedTo,
    string RecipientName,
    string Province,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CompletedAt,
    int ItemCount,
    DateTimeOffset CreatedAt);

public sealed record DeliveryAllocationResponse(
    Guid Id,
    Guid InventoryLotId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    long AllocatedBaseQuantity,
    long DeliveredBaseQuantity,
    long ReleasedBaseQuantity,
    string Status);

public sealed record DeliveryItemResponse(
    Guid Id,
    Guid OrderItemId,
    string Sku,
    string ProductName,
    string PackagingName,
    long PlannedQuantity,
    long PlannedBaseQuantity,
    long DeliveredBaseQuantity,
    long CancelledBaseQuantity,
    long RemainingBaseQuantity,
    string Status,
    IReadOnlyList<DeliveryAllocationResponse> Allocations);

public sealed record DeliveryAttemptItemResponse(
    Guid AllocationId,
    Guid InventoryLotId,
    string? LotNumber,
    long AttemptedBaseQuantity,
    long DeliveredBaseQuantity,
    long FailedBaseQuantity);

public sealed record DeliveryAttemptResponse(
    Guid Id,
    int AttemptNumber,
    string Status,
    DeliveryStaffReference AttemptedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ReceiverName,
    string? ProofImageUrl,
    string? FailureReasonCode,
    string? Note,
    Guid? SaleStockMovementId,
    IReadOnlyList<DeliveryAttemptItemResponse> Items);

public sealed record DeliveryResponse(
    Guid Id,
    string DeliveryNumber,
    Guid OrderId,
    string OrderNumber,
    string Status,
    DeliveryStaffReference? AssignedTo,
    DeliveryAddressResponse DeliveryAddress,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CompletedAt,
    string? Note,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<DeliveryItemResponse> Items,
    IReadOnlyList<DeliveryAttemptResponse> Attempts);

// Items null = everything still undelivered on the delivery.
public sealed record AttemptItemRequest(Guid AllocationId, long AttemptedBaseQuantity);

public sealed record StartAttemptRequest(IReadOnlyList<AttemptItemRequest>? Items = null);

// A missing allocation = delivered 0.
public sealed record DeliveredItemRequest(Guid AllocationId, long DeliveredBaseQuantity);

public sealed record CompleteAttemptRequest(
    IReadOnlyList<DeliveredItemRequest>? Items = null,
    string? ReceiverName = null,
    string? ProofImageUrl = null,
    string? FailureReasonCode = null,
    string? Note = null);

public sealed record CancelAttemptRequest(string Reason);

public sealed record ReportIncidentRequest(
    string IncidentType,
    string Description,
    Guid? DeliveryAttemptId = null,
    Guid? AllocationId = null,
    long? AffectedBaseQuantity = null,
    string? EvidenceImageUrl = null);

public sealed record ResolveIncidentRequest(
    string ResolutionType,
    string? ResolutionNote = null,
    Guid? RelatedStockMovementId = null);

public sealed record DeliveryIncidentResponse(
    Guid Id,
    Guid DeliveryId,
    Guid? DeliveryAttemptId,
    Guid? AllocationId,
    string IncidentType,
    long? AffectedBaseQuantity,
    string Description,
    string Status,
    string? ResolutionType,
    string? ResolutionNote,
    string? EvidenceImageUrl,
    Guid? RelatedStockMovementId,
    Guid ReportedBy,
    DateTimeOffset ReportedAt,
    Guid? ResolvedBy,
    DateTimeOffset? ResolvedAt);

// F2.7: what a Farmer sees of their order's deliveries — no lots, costs or internal notes; packaging units.
public sealed record MyDeliveryStaff(string FullName, string? PhoneNumber);

public sealed record MyDeliveryItem(
    Guid OrderItemId,
    string ProductName,
    string PackagingName,
    long PlannedQuantity,
    long DeliveredQuantity,
    long RemainingQuantity);

public sealed record MyDeliveryAttempt(
    int AttemptNumber,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ReceiverName,
    string? ProofImageUrl,
    string? FailureReasonCode);

public sealed record MyDeliveryResponse(
    Guid Id,
    string DeliveryNumber,
    string Status,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CompletedAt,
    MyDeliveryStaff? AssignedTo,
    IReadOnlyList<MyDeliveryItem> Items,
    IReadOnlyList<MyDeliveryAttempt> Attempts);

public interface IDeliveryService
{
    Task<DeliveryResponse> CreateAsync(CreateDeliveryRequest request, CancellationToken cancellationToken);

    Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListRequest request, CancellationToken cancellationToken);

    Task<DeliveryResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryListItem>> ListForOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<DeliveryResponse> AssignAsync(Guid id, AssignDeliveryRequest request, CancellationToken cancellationToken);

    Task<DeliveryResponse> ChangeLotsAsync(Guid id, Guid itemId, ChangeLotsRequest request, CancellationToken cancellationToken);

    Task<DeliveryResponse> DispatchAsync(Guid id, CancellationToken cancellationToken);

    Task<DeliveryResponse> CancelAsync(Guid id, CancelDeliveryRequest request, CancellationToken cancellationToken);
}

public interface IDeliveryAttemptService
{
    Task<DeliveryAttemptResponse> StartAsync(Guid deliveryId, StartAttemptRequest request, CancellationToken cancellationToken);

    Task<DeliveryResponse> CompleteAsync(Guid deliveryId, Guid attemptId, CompleteAttemptRequest request, CancellationToken cancellationToken);

    Task<DeliveryResponse> CancelAsync(Guid deliveryId, Guid attemptId, CancelAttemptRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryAttemptResponse>> ListAsync(Guid deliveryId, CancellationToken cancellationToken);
}

public interface IDeliveryIncidentService
{
    Task<DeliveryIncidentResponse> ReportAsync(Guid deliveryId, ReportIncidentRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryIncidentResponse>> ListAsync(Guid deliveryId, CancellationToken cancellationToken);

    Task<DeliveryIncidentResponse> ResolveAsync(Guid deliveryId, Guid incidentId, ResolveIncidentRequest request, CancellationToken cancellationToken);
}

public interface IMyDeliveryService
{
    Task<IReadOnlyList<MyDeliveryResponse>> ListForMyOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
