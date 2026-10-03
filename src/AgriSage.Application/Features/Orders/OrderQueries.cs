using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Reads an order as the shared OrderResponse (api-flows README §2). Flow L2 reuses it for the Farmer's own orders.
public sealed class OrderQueries(IAgriSageDbContext context)
{
    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var order = await context.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        return ToResponse(order);
    }

    public static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.OrderNumber,
        EnumText.Format(order.Source),
        EnumText.Format(order.CustomerType),
        order.FarmerProfileId,
        order.CustomerNameSnapshot,
        order.CustomerPhoneSnapshot,
        order.CustomerGroupIdSnapshot,
        order.PriceListIdSnapshot,
        EnumText.Format(order.SettlementType),
        order.CreditTermDaysSnapshot,
        EnumText.Format(order.FulfillmentType),
        order.DeliveryAddressLine is null
            ? null
            : new DeliveryAddressResponse(
                order.RecipientNameSnapshot!, order.RecipientPhoneSnapshot!, order.DeliveryAddressLine, order.DeliveryWard,
                order.DeliveryDistrict, order.DeliveryProvince!, order.DeliveryLatitude, order.DeliveryLongitude),
        EnumText.Format(order.Status),
        order.SubtotalAmount,
        order.TotalAmount,
        order.Note,
        order.CreatedBy,
        order.CreatedAt,
        order.ConfirmedBy,
        order.ConfirmedAt,
        order.PickupCompletedBy,
        order.PickupCompletedAt,
        order.CompletedAt,
        order.CancelledBy,
        order.CancelledAt,
        order.CancelReason,
        order.Version,
        order.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(ToResponse).ToList());

    private static OrderItemResponse ToResponse(OrderItem item) => new(
        item.Id,
        item.StoreProductId,
        item.ProductPackagingId,
        item.ProductSkuSnapshot,
        item.ProductNameSnapshot,
        item.PackagingNameSnapshot,
        item.Quantity,
        item.ConversionToBaseSnapshot,
        item.BaseQuantity,
        item.SuggestedUnitPrice,
        item.UnitPrice,
        item.LineTotalAmount,
        item.PriceOverridden,
        item.OverrideReason,
        item.OverriddenBy,
        item.FulfilledBaseQuantity,
        item.CancelledBaseQuantity,
        item.RemainingBaseQuantity,
        EnumText.Format(item.Status));
}
