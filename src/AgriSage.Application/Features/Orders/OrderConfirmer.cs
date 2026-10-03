using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// The lots the staff chose instead of FEFO (quick counter sale): the picks of one order item must add up to its base quantity.
public sealed record LotPick(Guid OrderItemId, Guid InventoryLotId, long BaseQuantity);

// Shared step (api-flows README §3.2 / §4.9; task F1.4): the core of "confirm an order". It works on a tracked order
// (its items loaded) inside the caller's transaction and never saves:
//   settlement guard → eligible lots → lock their balances (id order) → FEFO (or the chosen lots) → reserve → order.Confirm.
// Any line that cannot be covered refuses the whole confirmation (422, `errors` per line) and nothing is reserved.
public sealed class OrderConfirmer(
    IAgriSageDbContext context,
    IRowLockService locks,
    IOrderSettlementGuard settlementGuard,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AuditTrail audit)
{
    public async Task<InventoryReservation> ConfirmCoreAsync(
        Order order, IReadOnlyList<LotPick>? chosenLots, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);

        if (order.Status != OrderStatus.PendingConfirmation)
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; only PENDING_CONFIRMATION orders can be confirmed.");
        }

        var items = order.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();
        if (items.Count == 0)
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' has no items to confirm.");
        }

        if (await context.InventoryReservations.AnyAsync(
                r => r.OrderId == order.Id
                    && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed),
                cancellationToken))
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' already has an open stock reservation.");
        }

        await EnsureLinesAreStillSellableAsync(items, cancellationToken);

        // Payment / credit next: a refusal here reserves nothing.
        var settlement = await settlementGuard.EnsureCanConfirmAsync(order, actorId, cancellationToken);

        var lotIds = chosenLots is null
            ? await EligibleLotIdsAsync(items, today, cancellationToken)
            : chosenLots.Select(p => p.InventoryLotId).Distinct().ToList();
        await locks.LockLotBalancesAsync(lotIds, cancellationToken);

        // Read after the lock, so the balances are the committed ones.
        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        var allocations = chosenLots is null
            ? FefoAllocator.Allocate(
                items.Select(i => new FefoDemand(i.Id, i.StoreProductId, i.BaseQuantity)).ToList(),
                lots.Values.Where(l => l.IsEligibleForSale(today) && l.Balance.AvailableQuantity > 0)
                    .Select(l => new FefoLot(l.Id, l.StoreProductId, l.ExpiryDate, l.CreatedAt, l.Balance.AvailableQuantity)).ToList())
            : AllocationsFromChosenLots(items, chosenLots, lots, today);

        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < items.Count; index++)
        {
            var allocation = allocations.Single(a => a.OrderItemId == items[index].Id);
            if (allocation.ShortageBaseQuantity != 0)
            {
                // Negative only for chosen lots that add up to more than the line needs.
                errors[$"items[{index}]"] =
                [
                    allocation.ShortageBaseQuantity > 0
                        ? $"{items[index].ProductSkuSnapshot}: {allocation.ShortageBaseQuantity} base units short of the {items[index].BaseQuantity} needed."
                        : $"{items[index].ProductSkuSnapshot}: the chosen lots add up to more than the {items[index].BaseQuantity} needed."
                ];
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException(
                $"Not enough sellable stock for {errors.Count} line(s); nothing was reserved.", errors);
        }

        var reservation = new InventoryReservation(order.StoreId, order.Id, actorId, now);
        foreach (var allocation in allocations)
        {
            foreach (var pick in allocation.Picks)
            {
                lots[pick.LotId].Reserve(pick.BaseQuantity, today);
                reservation.AddItem(allocation.OrderItemId, pick.LotId, pick.BaseQuantity);
            }
        }

        context.InventoryReservations.Add(reservation);
        order.Confirm(actorId, now, settlement.CreditTermDays);
        audit.Record(
            "ORDER_CONFIRMED",
            "ORDER",
            order.Id,
            order.StoreId,
            null,
            new
            {
                order.OrderNumber,
                creditTermDays = settlement.CreditTermDays,
                reserved = reservation.Items.Select(i => new { orderItemId = i.OrderItemId, lotId = i.InventoryLotId, baseQuantity = i.BaseQuantityReserved })
            });

        return reservation;
    }

    // The lines were checked when the order was built; the product or packaging may have been switched off since. The
    // price stays the snapshot taken then. After confirmation the order is a commitment and is not checked again.
    private async Task EnsureLinesAreStillSellableAsync(List<OrderItem> items, CancellationToken cancellationToken)
    {
        var storeProductIds = items.Select(i => i.StoreProductId).Distinct().ToList();
        var packagingIds = items.Select(i => i.ProductPackagingId).Distinct().ToList();
        var storeProducts = await context.StoreProducts.AsNoTracking().Include(sp => sp.Product)
            .Where(sp => storeProductIds.Contains(sp.Id))
            .ToDictionaryAsync(sp => sp.Id, cancellationToken);
        var packagings = await context.ProductPackagings.AsNoTracking()
            .Where(p => packagingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var problem = !storeProducts.TryGetValue(item.StoreProductId, out var storeProduct)
                ? "The product no longer exists in the store."
                : !packagings.TryGetValue(item.ProductPackagingId, out var packaging)
                    ? "The packaging no longer exists."
                    : OrderBuilder.GetSellabilityProblem(storeProduct, packaging);

            if (problem is not null)
            {
                errors[$"items[{index}]"] = [$"{item.ProductSkuSnapshot}: {problem}"];
            }
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException(
                $"{errors.Count} line(s) can no longer be sold; remove them or cancel the order. Nothing was reserved.", errors);
        }
    }

    private async Task<List<Guid>> EligibleLotIdsAsync(List<OrderItem> items, DateOnly today, CancellationToken cancellationToken)
    {
        var productIds = items.Select(i => i.StoreProductId).Distinct().ToList();

        return await context.InventoryLots.AsNoTracking()
            .Where(l => productIds.Contains(l.StoreProductId)
                && l.Status == InventoryLotStatus.Active
                && (l.ExpiryDate == null || l.ExpiryDate >= today)
                && l.Balance.QuantityOnHand - l.Balance.QuantityReserved > 0)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    // Explicit lots: each must belong to the item's product, be sellable and have enough left for all the picks on it;
    // an item's picks must add up to its base quantity. A pick that cannot stand counts as not covered (a shortage of that line).
    private static List<FefoAllocation> AllocationsFromChosenLots(
        List<OrderItem> items, IReadOnlyList<LotPick> chosenLots, Dictionary<Guid, InventoryLot> lots, DateOnly today)
    {
        var left = lots.ToDictionary(l => l.Key, l => l.Value.IsEligibleForSale(today) ? l.Value.Balance.AvailableQuantity : 0);
        var result = new List<FefoAllocation>(items.Count);

        // The same lot named twice for a line counts once (a reservation has one line per item and lot).
        chosenLots = chosenLots
            .GroupBy(p => (p.OrderItemId, p.InventoryLotId))
            .Select(g => new LotPick(g.Key.OrderItemId, g.Key.InventoryLotId, g.Sum(p => p.BaseQuantity)))
            .ToList();

        foreach (var item in items)
        {
            var picks = new List<FefoPick>();
            var covered = 0L;

            foreach (var pick in chosenLots.Where(p => p.OrderItemId == item.Id))
            {
                if (pick.BaseQuantity <= 0
                    || !lots.TryGetValue(pick.InventoryLotId, out var lot)
                    || lot.StoreProductId != item.StoreProductId
                    || pick.BaseQuantity > left[pick.InventoryLotId])
                {
                    continue;
                }

                picks.Add(new FefoPick(pick.InventoryLotId, pick.BaseQuantity));
                left[pick.InventoryLotId] -= pick.BaseQuantity;
                covered += pick.BaseQuantity;
            }

            // Picking more than the line needs is as wrong as picking less (negative shortage).
            result.Add(new FefoAllocation(item.Id, picks, item.BaseQuantity - covered));
        }

        return result;
    }
}
