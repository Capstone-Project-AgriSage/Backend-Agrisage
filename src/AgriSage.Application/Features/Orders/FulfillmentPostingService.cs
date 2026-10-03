using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// One physical lot handed over for an order item (base units): what the staff actually took from the shelf.
public sealed record FulfillmentLine(Guid OrderItemId, Guid InventoryLotId, long BaseQuantity);

public sealed record FulfillmentPostingResult(StockMovement Movement, IReadOnlyList<FulfilledLine> Lines);

// Shared step (api-flows README §3.2 / §4.9; task F1.5, FLOW_1 §7), reused by delivery (F2.6) and the quick sale (F1.7).
// Inside the caller's transaction, on a tracked order with its items loaded; it never saves:
//   check lines → lock the lot balances (id order) → reject unsellable lots → SALE movement, one item per issue with the
//   cost snapshot (weighted average) → decrease on hand and reserved → consume (or move) the reservation →
//   order.RecordFulfillment → IFulfillmentFinancialPosting → post the movement.
// A reserved lot is issued from its reservation. For another lot the reservation moves there first (reserve in that lot,
// release the same quantity elsewhere) and is consumed there, so the reservation ends CONSUMED and the reserved
// quantities of the lots stay what the open orders still hold.
public sealed class FulfillmentPostingService(
    IAgriSageDbContext context,
    IRowLockService locks,
    IFulfillmentFinancialPosting financialPosting)
{
    public async Task<FulfillmentPostingResult> PostAsync(
        Order order,
        IReadOnlyList<FulfillmentLine> lines,
        FulfillmentSource source,
        Guid? deliveryId,
        Guid? deliveryAttemptId,
        Guid actorId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var today = BusinessCalendar.Today(at);

        if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForFulfillment or OrderStatus.PartiallyFulfilled))
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; it can be fulfilled only when CONFIRMED, PREPARING, READY_FOR_FULFILLMENT or PARTIALLY_FULFILLED.");
        }

        var items = order.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();
        var picks = lines
            .GroupBy(l => (l.OrderItemId, l.InventoryLotId))
            .Select(g => new FulfillmentLine(g.Key.OrderItemId, g.Key.InventoryLotId, g.Sum(l => l.BaseQuantity)))
            .ToList();
        if (picks.Count == 0)
        {
            throw new BusinessRuleException("Nothing to hand over: send at least one lot with a quantity.");
        }

        var errors = CheckQuantities(items, picks);
        if (errors.Count > 0)
        {
            throw new BusinessRuleException($"{errors.Count} line(s) cannot be handed over; nothing was saved.", errors);
        }

        var reservation = await context.InventoryReservations.Include(r => r.Items)
            .FirstOrDefaultAsync(
                r => r.OrderId == order.Id
                    && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed),
                cancellationToken)
            ?? throw new BusinessRuleException($"Order '{order.OrderNumber}' has no open stock reservation.");

        var pickedItemIds = picks.Select(p => p.OrderItemId).ToHashSet();
        var lotIds = picks.Select(p => p.InventoryLotId)
            .Concat(reservation.Items.Where(i => !i.IsDeleted && pickedItemIds.Contains(i.OrderItemId)).Select(i => i.InventoryLotId))
            .Distinct().ToList();
        await locks.LockLotBalancesAsync(lotIds, cancellationToken);

        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        errors = CheckLots(items, picks, lots, today);
        if (errors.Count > 0)
        {
            throw new BusinessRuleException($"{errors.Count} line(s) cannot be handed over; nothing was saved.", errors);
        }

        var movement = new StockMovement(
            order.StoreId,
            await DocumentNumbers.NextMovementNumberAsync(context, order.StoreId, today, cancellationToken),
            StockMovementType.Sale,
            at,
            actorId,
            orderId: order.Id,
            deliveryId: deliveryId);

        var pickedLotIds = picks.Select(p => p.InventoryLotId).ToHashSet();
        var ordered = picks
            .OrderByDescending(p => reservation.Items.Any(i => !i.IsDeleted && i.OrderItemId == p.OrderItemId
                && i.InventoryLotId == p.InventoryLotId && i.RemainingQuantity > 0))
            .ThenBy(p => items.FindIndex(i => i.Id == p.OrderItemId))
            .ThenBy(p => p.InventoryLotId);

        foreach (var pick in ordered)
        {
            var lot = lots[pick.InventoryLotId];
            var held = reservation.Items.Where(i => !i.IsDeleted && i.OrderItemId == pick.OrderItemId).ToList();
            var here = held.FirstOrDefault(i => i.InventoryLotId == pick.InventoryLotId && i.RemainingQuantity > 0);
            var fromReservation = here is null ? 0 : Math.Min(pick.BaseQuantity, here.RemainingQuantity);

            if (fromReservation > 0)
            {
                movement.AddItem(lot.Id, lot.IssueReserved(fromReservation, today));
                reservation.Consume(here!.Id, fromReservation);
            }

            var rest = pick.BaseQuantity - fromReservation;
            if (rest == 0)
            {
                continue;
            }

            // Not (all) from the reserved lot: the reservation moves to the lot handed over (reserve there, which needs
            // the stock free; release the same quantity of the line's reservation elsewhere) and is consumed there.
            // So the reservation ends CONSUMED, not RELEASED, and no other order's reservation is ever touched.
            lot.Reserve(rest, today);
            var target = reservation.ReserveMore(pick.OrderItemId, pick.InventoryLotId, rest);
            var toRelease = rest;
            foreach (var other in held.Where(i => i.InventoryLotId != pick.InventoryLotId && i.RemainingQuantity > 0)
                         .OrderBy(i => pickedLotIds.Contains(i.InventoryLotId) ? 1 : 0).ThenBy(i => i.Id))
            {
                if (toRelease == 0)
                {
                    break;
                }

                var release = Math.Min(toRelease, other.RemainingQuantity);
                lots[other.InventoryLotId].ReleaseReservation(release);
                reservation.Release(other.Id, release, actorId, at, "Another lot was handed over instead.");
                toRelease -= release;
            }

            if (toRelease > 0)
            {
                throw new BusinessRuleException("The stock reserved for a line is smaller than the quantity handed over.");
            }

            reservation.Consume(target.Id, rest);
            movement.AddItem(lot.Id, lot.IssueReserved(rest, today));
        }

        var fulfilled = new List<FulfilledLine>();
        foreach (var group in picks.GroupBy(p => p.OrderItemId))
        {
            var item = items.Single(i => i.Id == group.Key);
            var quantity = group.Sum(p => p.BaseQuantity);

            order.RecordFulfillment(item.Id, quantity, actorId, at);
            // Whole packages only, so the division is exact; multiply first anyway (rounding rule #61).
            fulfilled.Add(new FulfilledLine(item.Id, quantity, CostRounding.RoundMoney(quantity * item.UnitPrice / item.ConversionToBaseSnapshot)));
        }

        context.StockMovements.Add(movement);
        await financialPosting.PostAsync(
            new FulfillmentPostingContext(order, fulfilled, source, deliveryId, deliveryAttemptId, movement.Id, actorId, at),
            cancellationToken);
        movement.Post(actorId, at);

        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.PartiallyCancelled
            && reservation.RemainingQuantity > 0)
        {
            await ReleaseLeftoversAsync(reservation, lots, actorId, at, cancellationToken);
        }

        return new FulfillmentPostingResult(movement, fulfilled);
    }

    // Quantities per order item: the item is on the order, the lots add up to at most what is left, whole packages only.
    private static Dictionary<string, string[]> CheckQuantities(List<OrderItem> items, List<FulfillmentLine> picks)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var group in picks.GroupBy(p => p.OrderItemId))
        {
            var index = items.FindIndex(i => i.Id == group.Key);
            if (index < 0)
            {
                errors[$"orderItems[{group.Key}]"] = ["The item is not on this order."];
                continue;
            }

            var item = items[index];
            var messages = new List<string>();
            if (group.Any(p => p.BaseQuantity <= 0))
            {
                messages.Add("Every quantity must be positive.");
            }

            if (group.Any(p => p.BaseQuantity % item.ConversionToBaseSnapshot != 0))
            {
                messages.Add($"Quantities must be whole packages of {item.ConversionToBaseSnapshot} base units.");
            }

            if (group.Sum(p => p.BaseQuantity) > item.RemainingBaseQuantity)
            {
                messages.Add($"{group.Sum(p => p.BaseQuantity)} base units exceed the {item.RemainingBaseQuantity} still to hand over.");
            }

            if (messages.Count > 0)
            {
                errors[$"items[{index}]"] = [$"{item.ProductSkuSnapshot}: {string.Join(' ', messages)}"];
            }
        }

        return errors;
    }

    // The lots exist, belong to the item's product and can be sold today (expired, blocked and quarantined lots cannot).
    private static Dictionary<string, string[]> CheckLots(
        List<OrderItem> items, List<FulfillmentLine> picks, Dictionary<Guid, InventoryLot> lots, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var pick in picks)
        {
            var index = items.FindIndex(i => i.Id == pick.OrderItemId);
            var item = items[index];
            var problem = !lots.TryGetValue(pick.InventoryLotId, out var lot)
                ? "The lot does not exist."
                : lot.StoreProductId != item.StoreProductId
                    ? "The lot belongs to another product."
                    : !lot.IsEligibleForSale(today)
                        ? $"Lot '{lot.LotNumber ?? lot.Id.ToString()}' cannot be sold (status {EnumText.Format(lot.Status)}, expiry {lot.ExpiryDate:yyyy-MM-dd})."
                        : null;

            if (problem is not null)
            {
                errors[$"items[{index}]"] = [$"{item.ProductSkuSnapshot}: {problem}"];
            }
        }

        return errors;
    }

    // A finished order holds no stock: whatever is still reserved goes back (those lots are locked and loaded first).
    private async Task ReleaseLeftoversAsync(
        InventoryReservation reservation, Dictionary<Guid, InventoryLot> lots, Guid actorId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var open = reservation.Items.Where(i => !i.IsDeleted && i.RemainingQuantity > 0).ToList();
        var missing = open.Select(i => i.InventoryLotId).Where(id => !lots.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
        {
            await locks.LockLotBalancesAsync(missing, cancellationToken);
            foreach (var lot in await context.InventoryLots.Include(l => l.Balance).Where(l => missing.Contains(l.Id)).ToListAsync(cancellationToken))
            {
                lots[lot.Id] = lot;
            }
        }

        foreach (var line in open)
        {
            lots[line.InventoryLotId].ReleaseReservation(line.RemainingQuantity);
        }

        reservation.ReleaseRemaining(actorId, at, "The order has nothing left to hand over.");
    }
}
