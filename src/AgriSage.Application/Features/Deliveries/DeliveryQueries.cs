using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Orders;
using AgriSage.Domain.Features.Deliveries.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// Reads deliveries as DeliveryResponse / DeliveryListItem. Callers check the actor's scope first (DeliveryAccess).
public sealed class DeliveryQueries(IAgriSageDbContext context)
{
    public async Task<DeliveryResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, cancellationToken);
        var order = await context.Orders.AsNoTracking().Include(o => o.Items)
            .FirstAsync(o => o.Id == delivery.OrderId, cancellationToken);
        var orderItems = order.Items.ToDictionary(i => i.Id);

        var allocations = delivery.Items.Where(i => !i.IsDeleted).SelectMany(i => i.LotAllocations).ToList();
        var lotIds = allocations.Select(a => a.InventoryLotId).Distinct().ToList();
        var lots = await context.InventoryLots.AsNoTracking().Where(l => lotIds.Contains(l.Id))
            .Select(l => new { l.Id, l.LotNumber, l.ExpiryDate }).ToDictionaryAsync(l => l.Id, cancellationToken);
        var allocationLots = allocations.ToDictionary(a => a.Id, a => a.InventoryLotId);

        var memberIds = delivery.Attempts.Select(a => a.AttemptedByMemberId)
            .Append(delivery.AssignedToMemberId ?? Guid.Empty).Distinct().ToList();
        var staff = await StaffAsync(memberIds, cancellationToken);

        var items = delivery.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i =>
        {
            var orderItem = orderItems[i.OrderItemId];
            return new DeliveryItemResponse(
                i.Id, i.OrderItemId, orderItem.ProductSkuSnapshot, orderItem.ProductNameSnapshot, orderItem.PackagingNameSnapshot,
                i.PlannedQuantity, i.PlannedBaseQuantity, i.DeliveredBaseQuantity, i.CancelledBaseQuantity, i.RemainingBaseQuantity,
                EnumText.Format(i.Status),
                i.LotAllocations.Where(a => !a.IsDeleted).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a =>
                    new DeliveryAllocationResponse(
                        a.Id, a.InventoryLotId, lots.GetValueOrDefault(a.InventoryLotId)?.LotNumber,
                        lots.GetValueOrDefault(a.InventoryLotId)?.ExpiryDate, a.AllocatedBaseQuantity, a.DeliveredBaseQuantity,
                        a.ReleasedBaseQuantity, EnumText.Format(a.Status))).ToList());
        }).ToList();

        var attempts = delivery.Attempts.Where(a => !a.IsDeleted).OrderBy(a => a.AttemptNumber)
            .Select(a => ToAttempt(a, staff, allocationLots, lots.ToDictionary(l => l.Key, l => l.Value.LotNumber))).ToList();

        return new DeliveryResponse(
            delivery.Id, delivery.DeliveryNumber, delivery.OrderId, order.OrderNumber, EnumText.Format(delivery.Status),
            delivery.AssignedToMemberId is { } member ? staff.GetValueOrDefault(member) : null,
            new DeliveryAddressResponse(
                delivery.RecipientNameSnapshot, delivery.RecipientPhoneSnapshot, delivery.AddressLineSnapshot, delivery.WardSnapshot,
                delivery.DistrictSnapshot, delivery.ProvinceSnapshot, delivery.LatitudeSnapshot, delivery.LongitudeSnapshot),
            delivery.ScheduledAt, delivery.DispatchedAt, delivery.CompletedAt, delivery.Note, delivery.CreatedBy, delivery.CreatedAt,
            delivery.CancelledBy, delivery.CancelledAt, delivery.CancelReason, items, attempts);
    }

    public async Task<IReadOnlyList<DeliveryAttemptResponse>> AttemptsAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        (await GetAsync(deliveryId, cancellationToken)).Attempts;

    public async Task<IReadOnlyList<DeliveryListItem>> ListItemsAsync(IQueryable<Delivery> deliveries, CancellationToken cancellationToken)
    {
        var rows = await deliveries.AsNoTracking()
            .Select(d => new
            {
                d.Id, d.DeliveryNumber, d.OrderId,
                OrderNumber = context.Orders.Where(o => o.Id == d.OrderId).Select(o => o.OrderNumber).First(),
                d.Status, d.AssignedToMemberId, d.RecipientNameSnapshot, d.ProvinceSnapshot, d.ScheduledAt, d.DispatchedAt,
                d.CompletedAt, ItemCount = d.Items.Count(), d.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var staff = await StaffAsync(rows.Where(r => r.AssignedToMemberId != null).Select(r => r.AssignedToMemberId!.Value).ToList(),
            cancellationToken);

        return rows.Select(r => new DeliveryListItem(
            r.Id, r.DeliveryNumber, r.OrderId, r.OrderNumber, EnumText.Format(r.Status),
            r.AssignedToMemberId is { } member ? staff.GetValueOrDefault(member) : null, r.RecipientNameSnapshot, r.ProvinceSnapshot,
            r.ScheduledAt, r.DispatchedAt, r.CompletedAt, r.ItemCount, r.CreatedAt)).ToList();
    }

    // Store member id → the member's user (name and phone).
    public async Task<Dictionary<Guid, DeliveryStaffReference>> StaffAsync(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken) =>
        await context.StoreMembers.AsNoTracking().IgnoreQueryFilters()
            .Where(m => memberIds.Contains(m.Id))
            .Select(m => new { m.Id, Staff = new DeliveryStaffReference(m.UserId, m.User.FullName, m.User.PhoneNumber) })
            .ToDictionaryAsync(m => m.Id, m => m.Staff, cancellationToken);

    private static DeliveryAttemptResponse ToAttempt(
        DeliveryAttempt attempt,
        Dictionary<Guid, DeliveryStaffReference> staff,
        Dictionary<Guid, Guid> allocationLots,
        Dictionary<Guid, string?> lotNumbers) =>
        new(attempt.Id, attempt.AttemptNumber, EnumText.Format(attempt.Status),
            staff.GetValueOrDefault(attempt.AttemptedByMemberId) ?? new DeliveryStaffReference(Guid.Empty, "", null),
            attempt.StartedAt, attempt.CompletedAt, attempt.ReceiverName, attempt.ProofImageUrl, attempt.FailureReasonCode,
            attempt.Note, attempt.SaleStockMovementId,
            attempt.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i =>
            {
                var lotId = allocationLots.GetValueOrDefault(i.DeliveryItemLotAllocationId);
                return new DeliveryAttemptItemResponse(i.DeliveryItemLotAllocationId, lotId, lotNumbers.GetValueOrDefault(lotId),
                    i.AttemptedBaseQuantity, i.DeliveredBaseQuantity, i.FailedBaseQuantity);
            }).ToList());

    private async Task<Delivery> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Deliveries.AsNoTracking()
            .Include(d => d.Items).ThenInclude(i => i.LotAllocations)
            .Include(d => d.Attempts).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
        ?? throw new NotFoundException("Delivery", id);
}
