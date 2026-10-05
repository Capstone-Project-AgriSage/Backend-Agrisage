using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Deliveries.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// FLOW_2 §7 (task F2.5): delivery notes. Several notes per order = several trips. Lots come from the order's stock
// reservation (F1.4); a delivery note never reserves or issues stock on its own except when its lots are changed, where
// the reservation follows the lots. Locks: order first, then the delivery.
public sealed class DeliveryService(
    IAgriSageDbContext context,
    DeliveryAccess access,
    DeliveryQueries queries,
    IRowLockService locks,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IDeliveryService
{
    private static readonly OrderStatus[] DeliverableOrderStatuses =
        [OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.ReadyForFulfillment, OrderStatus.PartiallyFulfilled];

    public async Task<DeliveryResponse> CreateAsync(CreateDeliveryRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);
        var now = clock.UtcNow;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(request.OrderId, cancellationToken);

        var order = await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.StoreId == actor.StoreId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);
        if (order.FulfillmentType != FulfillmentType.Delivery)
        {
            throw new BusinessRuleException($"Order '{order.OrderNumber}' is a PICKUP order; it is handed over at the counter.");
        }

        if (!DeliverableOrderStatuses.Contains(order.Status))
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; delivery notes are made for confirmed orders only.");
        }

        var reservation = await OpenReservationAsync(order, cancellationToken);
        var others = await context.Deliveries.AsNoTracking()
            .Include(d => d.Items).ThenInclude(i => i.LotAllocations)
            .Where(d => d.OrderId == order.Id && d.Status != DeliveryStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var otherItems = others.SelectMany(d => d.Items).Where(i => !i.IsDeleted).ToList();
        // Reserved stock already promised to other delivery notes and not delivered yet.
        var heldElsewhere = otherItems.SelectMany(i => i.LotAllocations)
            .Where(a => !a.IsDeleted && a.InventoryReservationItemId != null && a.UndeliveredQuantity > 0)
            .GroupBy(a => a.InventoryReservationItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.UndeliveredQuantity));
        var lotIds = reservation.Items.Select(i => i.InventoryLotId).Distinct().ToList();
        var expiries = await context.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.ExpiryDate, cancellationToken);

        var errors = new Dictionary<string, string[]>();
        var plans = new List<(OrderItem Item, long Quantity, List<(InventoryReservationItem Line, long Take)> Lots)>();
        for (var index = 0; index < request.Items.Count; index++)
        {
            var line = request.Items[index];
            var orderItem = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId && !i.IsDeleted);
            if (orderItem is null)
            {
                errors[$"items[{index}]"] = ["The item is not on this order."];
                continue;
            }

            var planned = checked(line.PlannedQuantity * orderItem.ConversionToBaseSnapshot);
            var onOtherNotes = otherItems.Where(i => i.OrderItemId == orderItem.Id).Sum(i => i.RemainingBaseQuantity);
            var free = orderItem.RemainingBaseQuantity - onOtherNotes;
            if (planned > free)
            {
                errors[$"items[{index}]"] =
                    [$"{orderItem.ProductSkuSnapshot}: {planned} base units exceed the {Math.Max(free, 0)} not yet planned on another delivery."];
                continue;
            }

            // FEFO over the order's reservation lines of this item, minus what other notes already hold.
            var candidates = reservation.Items
                .Where(i => !i.IsDeleted && i.OrderItemId == orderItem.Id && i.RemainingQuantity > 0)
                .Select(i => (Line: i, Free: i.RemainingQuantity - heldElsewhere.GetValueOrDefault(i.Id)))
                .Where(c => c.Free > 0)
                .OrderBy(c => expiries.GetValueOrDefault(c.Line.InventoryLotId) ?? DateOnly.MaxValue)
                .ThenBy(c => c.Line.InventoryLotId)
                .ToList();
            if (candidates.Sum(c => c.Free) < planned)
            {
                errors[$"items[{index}]"] =
                    [$"{orderItem.ProductSkuSnapshot}: only {candidates.Sum(c => c.Free)} reserved base units are free for a new delivery."];
                continue;
            }

            var take = new List<(InventoryReservationItem Line, long Take)>();
            var left = planned;
            foreach (var (reserved, available) in candidates)
            {
                if (left == 0)
                {
                    break;
                }

                var quantity = Math.Min(left, available);
                take.Add((reserved, quantity));
                left -= quantity;
            }

            plans.Add((orderItem, line.PlannedQuantity, take));
        }

        if (errors.Count > 0)
        {
            throw new BusinessRuleException($"{errors.Count} line(s) cannot be planned; nothing was saved.", errors);
        }

        var number = await DocumentNumbers.NextAsync(
            context.Deliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.StoreId == actor.StoreId).Select(d => d.DeliveryNumber),
            DocumentNumbers.Delivery, BusinessCalendar.Today(now), cancellationToken);
        var delivery = new Delivery(order, number, Address(request.DeliveryAddress, order), actor.UserId, request.ScheduledAt,
            Texts.Clean(request.Note));
        foreach (var (orderItem, quantity, lots) in plans)
        {
            var item = delivery.AddItem(orderItem, quantity);
            foreach (var (reserved, take) in lots)
            {
                delivery.AllocateLot(item.Id, reserved.InventoryLotId, take, reserved.Id);
            }
        }

        context.Deliveries.Add(delivery);
        audit.Record("DELIVERY_CREATED", "DELIVERY", delivery.Id, delivery.StoreId, null,
            new { delivery.DeliveryNumber, order.OrderNumber });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(delivery.Id, cancellationToken);
    }

    public async Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        var query = access.Scope(actor).AsNoTracking();

        if (EnumText.TryParse<DeliveryStatus>(request.Status, out var status))
        {
            query = query.Where(d => d.Status == status);
        }

        if (request.OrderId is { } orderId)
        {
            query = query.Where(d => d.OrderId == orderId);
        }

        if (request.AssignedToUserId is { } userId)
        {
            query = query.Where(d => d.AssignedToMember != null && d.AssignedToMember.UserId == userId);
        }

        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(d => (d.ScheduledAt ?? d.CreatedAt) >= start);
        }

        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(d => (d.ScheduledAt ?? d.CreatedAt) < end);
        }

        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLower();
            query = query.Where(d => d.DeliveryNumber.ToLower().Contains(term)
                || d.RecipientNameSnapshot.ToLower().Contains(term)
                || d.RecipientPhoneSnapshot.Contains(term)
                || context.Orders.Any(o => o.Id == d.OrderId && o.OrderNumber.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var page = query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.DeliveryNumber)
            .Skip(request.Skip).Take(request.PageSize);

        return new PagedResult<DeliveryListItem>(await queries.ListItemsAsync(page, cancellationToken), request.Page, request.PageSize, total);
    }

    public async Task<DeliveryResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        await access.EnsureVisibleAsync(actor, id, cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryListItem>> ListForOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);
        if (!await context.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.StoreId == actor.StoreId, cancellationToken))
        {
            throw new NotFoundException("Order", orderId);
        }

        return await queries.ListItemsAsync(
            context.Deliveries.Where(d => d.OrderId == orderId).OrderBy(d => d.CreatedAt).ThenBy(d => d.DeliveryNumber),
            cancellationToken);
    }

    // The client sends a staff user id; the server resolves the ACTIVE DELIVERY_STAFF store member (decision D2).
    public async Task<DeliveryResponse> AssignAsync(Guid id, AssignDeliveryRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var delivery = await LockAndLoadAsync(actor, id, cancellationToken);
        var memberId = await context.StoreMembers.AsNoTracking()
                .Where(m => m.StoreId == actor.StoreId && m.UserId == request.AssignedToUserId && m.Status == StoreMemberStatus.Active
                    && m.User.Role.Code == RoleCode.DeliveryStaff && m.User.Status == UserStatus.Active)
                .Select(m => (Guid?)m.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("Assign the delivery to an active DELIVERY_STAFF member of the store.");

        var before = delivery.AssignedToMemberId;
        delivery.Assign(memberId);
        audit.Record("DELIVERY_ASSIGNED", "DELIVERY", delivery.Id, delivery.StoreId,
            new { assignedToMemberId = before }, new { assignedToMemberId = memberId, assignedToUserId = request.AssignedToUserId });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    // Replaces the undelivered allocations of one item before dispatch. The old allocations are released (kept as
    // history) and the order's reservation follows the lots in the same transaction: the new lots are reserved first,
    // then the old ones released, so the reservation never runs empty in between and a lot kept in the new set only
    // changes by the difference.
    public async Task<DeliveryResponse> ChangeLotsAsync(Guid id, Guid itemId, ChangeLotsRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var orderId = await access.Scope(actor).AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.OrderId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Delivery", id);
        await locks.LockOrderAsync(orderId, cancellationToken);
        var delivery = await LockAndLoadAsync(actor, id, cancellationToken);

        var item = delivery.Items.FirstOrDefault(i => i.Id == itemId && !i.IsDeleted)
            ?? throw new NotFoundException("Delivery item", itemId);
        var requested = request.Lots.Sum(l => l.BaseQuantity);
        if (requested != item.RemainingBaseQuantity)
        {
            throw new BusinessRuleException(
                $"The lots must add up to the {item.RemainingBaseQuantity} base units of this item not yet delivered (got {requested}).");
        }

        var order = await context.Orders.AsNoTracking().Include(o => o.Items).FirstAsync(o => o.Id == orderId, cancellationToken);
        var orderItem = order.Items.First(i => i.Id == item.OrderItemId);
        var reservation = await OpenReservationAsync(order, cancellationToken);

        var oldAllocations = item.LotAllocations.Where(a => !a.IsDeleted && a.UndeliveredQuantity > 0).ToList();
        var oldByLot = oldAllocations.GroupBy(a => a.InventoryLotId).ToDictionary(g => g.Key, g => g.Sum(a => a.UndeliveredQuantity));
        var newByLot = request.Lots.ToDictionary(l => l.InventoryLotId, l => l.BaseQuantity);
        var lotIds = oldByLot.Keys.Concat(newByLot.Keys).Distinct().ToList();
        await locks.LockLotBalancesAsync(lotIds, cancellationToken);
        var lots = await context.InventoryLots.Include(l => l.Balance).Where(l => lotIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        foreach (var lotId in newByLot.Keys)
        {
            var problem = !lots.TryGetValue(lotId, out var lot) ? "does not exist"
                : lot.StoreProductId != orderItem.StoreProductId ? "belongs to another product"
                : !lot.IsEligibleForSale(today) ? $"cannot be sold (status {EnumText.Format(lot.Status)}, expiry {lot.ExpiryDate:yyyy-MM-dd})"
                : null;
            if (problem is not null)
            {
                throw new BusinessRuleException($"Lot '{lot?.LotNumber ?? lotId.ToString()}' {problem}.");
            }
        }

        var reason = $"Lots changed on delivery {delivery.DeliveryNumber}.";
        var newLines = new Dictionary<Guid, InventoryReservationItem>();
        foreach (var (lotId, quantity) in newByLot)
        {
            var extra = quantity - oldByLot.GetValueOrDefault(lotId);
            if (extra > 0)
            {
                lots[lotId].Reserve(extra, today);
            }

            newLines[lotId] = reservation.ReserveMore(orderItem.Id, lotId, quantity);
        }

        foreach (var allocation in oldAllocations)
        {
            var quantity = allocation.UndeliveredQuantity;
            var line = allocation.InventoryReservationItemId is { } lineId
                ? reservation.Items.First(i => i.Id == lineId)
                : reservation.Items.First(i => !i.IsDeleted && i.OrderItemId == orderItem.Id && i.InventoryLotId == allocation.InventoryLotId);
            delivery.ReleaseAllocation(allocation.Id);
            reservation.Release(line.Id, quantity, actor.UserId, now, reason);
        }

        foreach (var (lotId, quantity) in oldByLot)
        {
            var less = quantity - newByLot.GetValueOrDefault(lotId);
            if (less > 0)
            {
                lots[lotId].ReleaseReservation(less);
            }
        }

        foreach (var (lotId, quantity) in newByLot)
        {
            delivery.AllocateLot(item.Id, lotId, quantity, newLines[lotId].Id);
        }

        audit.Record("DELIVERY_LOTS_CHANGED", "DELIVERY", delivery.Id, delivery.StoreId,
            new { itemId, lots = oldByLot.Select(l => new { inventoryLotId = l.Key, baseQuantity = l.Value }) },
            new { itemId, lots = newByLot.Select(l => new { inventoryLotId = l.Key, baseQuantity = l.Value }) });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<DeliveryResponse> DispatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var delivery = await LockAndLoadAsync(actor, id, cancellationToken);
        var before = delivery.Status;
        delivery.Dispatch(clock.UtcNow);
        audit.Record("DELIVERY_DISPATCHED", "DELIVERY", delivery.Id, delivery.StoreId,
            new { status = EnumText.Format(before) }, new { status = EnumText.Format(delivery.Status) });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    // Only when no attempt is IN_PROGRESS. The undelivered remainder is released from the note; the order keeps its stock
    // reservation, so another delivery note (or cancel-remaining) can take it.
    public async Task<DeliveryResponse> CancelAsync(Guid id, CancelDeliveryRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.OperatorAsync(cancellationToken);
        var reason = request.Reason.Trim();

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        var delivery = await LockAndLoadAsync(actor, id, cancellationToken);
        var before = delivery.Status;
        delivery.Cancel(actor.UserId, clock.UtcNow, reason);
        audit.Record("DELIVERY_CANCELLED", "DELIVERY", delivery.Id, delivery.StoreId,
            new { status = EnumText.Format(before) }, new { status = EnumText.Format(delivery.Status) }, reason);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    private async Task<Delivery> LockAndLoadAsync(DeliveryAccess.Actor actor, Guid id, CancellationToken cancellationToken)
    {
        await locks.LockDeliveryAsync(id, cancellationToken);

        return await access.Scope(actor)
                .Include(d => d.Items).ThenInclude(i => i.LotAllocations)
                .Include(d => d.Attempts).ThenInclude(a => a.Items)
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Delivery", id);
    }

    private async Task<InventoryReservation> OpenReservationAsync(Order order, CancellationToken cancellationToken) =>
        await context.InventoryReservations.Include(r => r.Items)
            .FirstOrDefaultAsync(
                r => r.OrderId == order.Id
                    && (r.Status == InventoryReservationStatus.Active || r.Status == InventoryReservationStatus.PartiallyConsumed),
                cancellationToken)
        ?? throw new BusinessRuleException($"Order '{order.OrderNumber}' has no open stock reservation.");

    // The typed address (phone normalized), or a snapshot of the order's own address.
    private static DeliveryAddress Address(DeliveryAddressRequest? typed, Order order)
    {
        if (typed is null)
        {
            return new DeliveryAddress(
                order.RecipientNameSnapshot!, order.RecipientPhoneSnapshot!, order.DeliveryAddressLine!, order.DeliveryProvince!,
                order.DeliveryWard, order.DeliveryDistrict, order.DeliveryLatitude, order.DeliveryLongitude);
        }

        if (!ContactNormalizer.TryNormalizePhone(typed.RecipientPhone, out var phone))
        {
            throw new BusinessRuleException("The recipient phone must be a valid Vietnamese mobile number.");
        }

        return new DeliveryAddress(typed.RecipientName.Trim(), phone!, typed.AddressLine.Trim(), typed.Province.Trim(),
            Texts.Clean(typed.Ward), Texts.Clean(typed.District), typed.Latitude, typed.Longitude);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two delivery notes raced for the same number; the unique index decided.
            throw new ConflictException("Another delivery was created at the same time; try again.");
        }
    }
}
