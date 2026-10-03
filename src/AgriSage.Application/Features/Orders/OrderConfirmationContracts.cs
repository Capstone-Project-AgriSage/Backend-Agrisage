namespace AgriSage.Application.Features.Orders;

// FLOW_1 §6. For a PENDING order the lots are FEFO's proposal over the available stock; for a confirmed one they are the
// lines of its reservation that are still open.
public sealed record FefoLotSuggestion(
    Guid InventoryLotId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    long AvailableBaseQuantity,
    long SuggestedBaseQuantity);

public sealed record FefoItemSuggestion(
    Guid OrderItemId,
    long BaseQuantity,
    long RemainingBaseQuantity,
    IReadOnlyList<FefoLotSuggestion> Lots,
    long ShortageBaseQuantity);

public sealed record FefoSuggestionResponse(Guid OrderId, IReadOnlyList<FefoItemSuggestion> Items);

public sealed record ReservationItemResponse(
    Guid Id,
    Guid OrderItemId,
    Guid InventoryLotId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    long ReservedBaseQuantity,
    long ConsumedBaseQuantity,
    long ReleasedBaseQuantity,
    long RemainingBaseQuantity);

// Status is derived from the item quantities (database design §35.3), never set by a request.
public sealed record ReservationResponse(
    Guid Id,
    Guid OrderId,
    string Status,
    DateTimeOffset ReservedAt,
    Guid ReservedBy,
    DateTimeOffset? ReleasedAt,
    Guid? ReleasedBy,
    string? ReleaseReason,
    IReadOnlyList<ReservationItemResponse> Items);

public interface IOrderConfirmationService
{
    Task<FefoSuggestionResponse> GetFefoSuggestionsAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OrderResponse> ConfirmAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OrderResponse> StartPreparingAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OrderResponse> MarkReadyAsync(Guid orderId, CancellationToken cancellationToken);

    Task<ReservationResponse> GetReservationAsync(Guid orderId, CancellationToken cancellationToken);
}
