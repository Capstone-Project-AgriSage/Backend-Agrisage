using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Pickup at the counter and cancelling what is left of a line (task F1.5, FLOW_1 §7). Both own one transaction:
// lock the order → work → one SaveChanges → commit. Conflicting writes to the order or a lot balance are a 409.
public sealed class OrderPickupService(
    IAgriSageDbContext context,
    IRowLockService locks,
    FulfillmentPostingService posting,
    IOrderSettlementGuard settlementGuard,
    IOrderPaymentCancellation paymentCancellation,
    OrderQueries queries,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AuditTrail audit) : IOrderPickupService
{
    public async Task<OrderResponse> PickupAsync(Guid orderId, PickupRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var now = clock.UtcNow;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var order = await LockAndLoadAsync(orderId, cancellationToken);

        if (order.FulfillmentType != FulfillmentType.Pickup)
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' is a DELIVERY order; it is handed over by a delivery.");
        }

        var result = await posting.PostAsync(
            order,
            request.Items.SelectMany(i => i.Lots.Select(l => new FulfillmentLine(i.OrderItemId, l.InventoryLotId, l.BaseQuantity))).ToList(),
            FulfillmentSource.Pickup,
            null,
            null,
            actorId,
            now,
            cancellationToken);

        audit.Record(
            "ORDER_PICKED_UP",
            "ORDER",
            order.Id,
            order.StoreId,
            null,
            new { order.OrderNumber, movement = result.Movement.MovementNumber, status = EnumText.Format(order.Status) },
            Texts.Clean(request.Note));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(orderId, cancellationToken);
    }

    // The unfulfilled part of one line is dropped: its reservation goes back (no stock movement). When nothing stays open
    // the order is CANCELLED or PARTIALLY_CANCELLED: the credit reservation is released and the prepayment that will never
    // be consumed is returned through the cancellation flow (§8).
    public async Task<OrderResponse> CancelRemainingAsync(
        Guid orderId, Guid itemId, CancelRemainingRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var now = clock.UtcNow;
        var reason = request.Reason.Trim();

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var order = await LockAndLoadAsync(orderId, cancellationToken);

        if (order.Items.All(i => i.IsDeleted || i.Id != itemId))
        {
            throw new NotFoundException("Order item", itemId);
        }

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForFulfillment or OrderStatus.PartiallyFulfilled))
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; the rest of a line can be cancelled only after confirmation and before the order ends.");
        }

        order.CancelItemRemaining(itemId, actorId, now, reason);
        await ReleaseReservationOfItemAsync(order, itemId, actorId, now, reason, cancellationToken);

        await settlementGuard.ReleaseAsync(order, actorId, reason, cancellationToken);

        if (order.Status is OrderStatus.Cancelled or OrderStatus.PartiallyCancelled)
        {
            await paymentCancellation.ReverseForCancelledOrderAsync(order, actorId, reason, cancellationToken);
        }

        audit.Record(
            "ORDER_ITEM_REMAINING_CANCELLED", "ORDER", order.Id, order.StoreId, null,
            new { orderItemId = itemId, status = EnumText.Format(order.Status) }, reason);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(orderId, cancellationToken);
    }

    private async Task ReleaseReservationOfItemAsync(
        Order order, Guid itemId, Guid actorId, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var reservation = await context.InventoryReservations.Include(r => r.Items)
            .FirstOrDefaultAsync(
                r => r.OrderId == order.Id
                    && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed),
                cancellationToken);
        var open = reservation?.Items.Where(i => !i.IsDeleted && i.OrderItemId == itemId && i.RemainingQuantity > 0).ToList() ?? [];
        if (reservation is null || open.Count == 0)
        {
            return;
        }

        var lotIds = open.Select(i => i.InventoryLotId).Distinct().ToList();
        await locks.LockLotBalancesAsync(lotIds, cancellationToken);
        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        foreach (var line in open)
        {
            var quantity = line.RemainingQuantity;
            lots[line.InventoryLotId].ReleaseReservation(quantity);
            reservation.Release(line.Id, quantity, actorId, now, reason);
        }
    }

    private async Task<Order> LockAndLoadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        await locks.LockOrderAsync(orderId, cancellationToken);

        var order = await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        if (order.FarmerProfileId is { } farmer) await locks.LockFarmerProfileAsync(farmer, cancellationToken);
        return order;
    }
}
