using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Confirmation and stock reservation endpoints (task F1.4, FLOW_1 §6). Confirm owns one transaction: lock the order →
// OrderConfirmer core → one SaveChanges → commit. A conflicting write to a lot balance or to the order is a 409
// (DbUpdateConcurrencyException).
public sealed class OrderConfirmationService(
    IAgriSageDbContext context,
    IRowLockService locks,
    OrderConfirmer confirmer,
    OrderQueries queries,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IOrderConfirmationService
{
    public async Task<OrderResponse> ConfirmAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(orderId, cancellationToken);

        var order = await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        await confirmer.ConfirmCoreAsync(order, null, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two confirmations raced for the one open reservation of the order.
            throw new ConflictException("The order was confirmed at the same time; reload it.");
        }

        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(orderId, cancellationToken);
    }

    // CONFIRMED → PREPARING (optional step, decision D10).
    public async Task<OrderResponse> StartPreparingAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await FindAsync(orderId, cancellationToken);

        order.StartPreparing();
        audit.Record("ORDER_PREPARING_STARTED", "ORDER", order.Id, order.StoreId);
        await context.SaveChangesAsync(cancellationToken);

        return await queries.GetAsync(orderId, cancellationToken);
    }

    // CONFIRMED or PREPARING → READY_FOR_FULFILLMENT.
    public async Task<OrderResponse> MarkReadyAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await FindAsync(orderId, cancellationToken);

        order.MarkReadyForFulfillment();
        audit.Record("ORDER_MARKED_READY", "ORDER", order.Id, order.StoreId);
        await context.SaveChangesAsync(cancellationToken);

        return await queries.GetAsync(orderId, cancellationToken);
    }

    // PENDING: FEFO over the sellable stock, as confirmation would allocate it now. Confirmed: the open lines of the
    // reservation (the lots the stock is held in). Anything else needs no stock.
    public async Task<FefoSuggestionResponse> GetFefoSuggestionsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var order = await context.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        var items = order.Items.Where(i => !i.IsDeleted && i.RemainingBaseQuantity > 0)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();

        if (order.Status == OrderStatus.PendingConfirmation)
        {
            return await ProposeAsync(order, items, cancellationToken);
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.PartiallyCancelled or OrderStatus.Completed)
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; it needs no more stock.");
        }

        return await FromReservationAsync(order, items, cancellationToken);
    }

    public async Task<ReservationResponse> GetReservationAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var reservation = await context.InventoryReservations.AsNoTracking().Include(r => r.Items)
            .Where(r => r.OrderId == orderId && r.StoreId == storeId)
            .OrderByDescending(r => r.ReservedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Stock reservation of order", orderId);

        var lots = await LotsAsync(reservation.Items.Select(i => i.InventoryLotId).Distinct().ToList(), cancellationToken);

        return new ReservationResponse(
            reservation.Id,
            reservation.OrderId,
            EnumText.Format(reservation.Status),
            reservation.ReservedAt,
            reservation.ReservedBy,
            reservation.ReleasedAt,
            reservation.ReleasedBy,
            reservation.ReleaseReason,
            reservation.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => new ReservationItemResponse(
                i.Id,
                i.OrderItemId,
                i.InventoryLotId,
                lots.GetValueOrDefault(i.InventoryLotId)?.LotNumber,
                lots.GetValueOrDefault(i.InventoryLotId)?.ExpiryDate,
                i.BaseQuantityReserved,
                i.BaseQuantityConsumed,
                i.BaseQuantityReleased,
                i.RemainingQuantity)).ToList());
    }

    private async Task<FefoSuggestionResponse> ProposeAsync(Order order, List<OrderItem> items, CancellationToken cancellationToken)
    {
        var today = BusinessCalendar.Today(clock.UtcNow);
        var productIds = items.Select(i => i.StoreProductId).Distinct().ToList();
        var lots = await context.InventoryLots.AsNoTracking().Include(l => l.Balance)
            .Where(l => productIds.Contains(l.StoreProductId)
                && l.Status == InventoryLotStatus.Active
                && (l.ExpiryDate == null || l.ExpiryDate >= today)
                && l.Balance.QuantityOnHand - l.Balance.QuantityReserved > 0)
            .ToListAsync(cancellationToken);
        var byId = lots.ToDictionary(l => l.Id);

        var allocations = FefoAllocator.Allocate(
            items.Select(i => new FefoDemand(i.Id, i.StoreProductId, i.BaseQuantity)).ToList(),
            lots.Select(l => new FefoLot(l.Id, l.StoreProductId, l.ExpiryDate, l.CreatedAt, l.Balance.AvailableQuantity)).ToList());

        return new FefoSuggestionResponse(
            order.Id,
            items.Select(item =>
            {
                var allocation = allocations.Single(a => a.OrderItemId == item.Id);

                return new FefoItemSuggestion(
                    item.Id,
                    item.BaseQuantity,
                    item.RemainingBaseQuantity,
                    allocation.Picks.Select(p => new FefoLotSuggestion(
                        p.LotId, byId[p.LotId].LotNumber, byId[p.LotId].ExpiryDate, byId[p.LotId].Balance.AvailableQuantity, p.BaseQuantity)).ToList(),
                    allocation.ShortageBaseQuantity);
            }).ToList());
    }

    private async Task<FefoSuggestionResponse> FromReservationAsync(Order order, List<OrderItem> items, CancellationToken cancellationToken)
    {
        var reservation = await context.InventoryReservations.AsNoTracking().Include(r => r.Items)
            .Where(r => r.OrderId == order.Id
                && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed))
            .FirstOrDefaultAsync(cancellationToken);
        var lines = reservation?.Items.Where(i => !i.IsDeleted && i.RemainingQuantity > 0).ToList() ?? [];
        var lots = await LotsAsync(lines.Select(l => l.InventoryLotId).Distinct().ToList(), cancellationToken);

        return new FefoSuggestionResponse(
            order.Id,
            items.Select(item => new FefoItemSuggestion(
                item.Id,
                item.BaseQuantity,
                item.RemainingBaseQuantity,
                lines.Where(l => l.OrderItemId == item.Id)
                    .OrderBy(l => lots.GetValueOrDefault(l.InventoryLotId)?.ExpiryDate is null ? 1 : 0)
                    .ThenBy(l => lots.GetValueOrDefault(l.InventoryLotId)?.ExpiryDate)
                    .ThenBy(l => l.Id)
                    .Select(l => new FefoLotSuggestion(
                        l.InventoryLotId,
                        lots.GetValueOrDefault(l.InventoryLotId)?.LotNumber,
                        lots.GetValueOrDefault(l.InventoryLotId)?.ExpiryDate,
                        l.RemainingQuantity,
                        l.RemainingQuantity)).ToList(),
                0)).ToList());
    }

    private async Task<Dictionary<Guid, LotInfo>> LotsAsync(List<Guid> lotIds, CancellationToken cancellationToken) =>
        await context.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .Select(l => new LotInfo(l.Id, l.LotNumber, l.ExpiryDate))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

    private async Task<Order> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
    }

    private sealed record LotInfo(Guid Id, string? LotNumber, DateOnly? ExpiryDate);
}
