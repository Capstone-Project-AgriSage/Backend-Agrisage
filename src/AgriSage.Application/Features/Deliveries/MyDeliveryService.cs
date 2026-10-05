using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Deliveries.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// GET /api/me/orders/{id}/deliveries (FLOW_2 §9, task F2.7): a Farmer follows the deliveries of their own order. No lots,
// costs or internal notes; quantities in packaging units (deliveries carry whole packages). Someone else's order → 404.
public sealed class MyDeliveryService(IAgriSageDbContext context, CurrentFarmer currentFarmer, DeliveryQueries queries) : IMyDeliveryService
{
    public async Task<IReadOnlyList<MyDeliveryResponse>> ListForMyOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var order = await context.Orders.AsNoTracking().Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId && o.FarmerProfileId == farmer.FarmerProfileId,
                    cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        var orderItems = order.Items.ToDictionary(i => i.Id);

        var deliveries = await context.Deliveries.AsNoTracking()
            .Include(d => d.Items)
            .Include(d => d.Attempts)
            .Where(d => d.OrderId == orderId && d.Status != DeliveryStatus.Draft)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.DeliveryNumber)
            .ToListAsync(cancellationToken);
        var staff = await queries.StaffAsync(
            deliveries.Where(d => d.AssignedToMemberId != null).Select(d => d.AssignedToMemberId!.Value).Distinct().ToList(),
            cancellationToken);

        return deliveries.Select(d => new MyDeliveryResponse(
            d.Id, d.DeliveryNumber, EnumText.Format(d.Status), d.ScheduledAt, d.DispatchedAt, d.CompletedAt,
            d.AssignedToMemberId is { } member && staff.TryGetValue(member, out var person)
                ? new MyDeliveryStaff(person.FullName, person.PhoneNumber)
                : null,
            d.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => new MyDeliveryItem(
                i.OrderItemId, orderItems[i.OrderItemId].ProductNameSnapshot, orderItems[i.OrderItemId].PackagingNameSnapshot,
                i.PlannedQuantity, i.DeliveredBaseQuantity / i.ConversionToBaseSnapshot,
                i.RemainingBaseQuantity / i.ConversionToBaseSnapshot)).ToList(),
            d.Attempts.Where(a => !a.IsDeleted && a.Status != DeliveryAttemptStatus.Cancelled).OrderBy(a => a.AttemptNumber)
                .Select(a => new MyDeliveryAttempt(a.AttemptNumber, EnumText.Format(a.Status), a.StartedAt, a.CompletedAt,
                    a.ReceiverName, a.ProofImageUrl, a.FailureReasonCode)).ToList())).ToList();
    }
}
