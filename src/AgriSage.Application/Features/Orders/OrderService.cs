using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Orders;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Staff counter orders (task F1.2, FLOW_1 §4). Each write is one SaveChanges, so the order, its items and their audit
// rows are saved together. Items, header and prices change only while PENDING_CONFIRMATION (the Order enforces it).
// Concurrent edits of one order are caught by its version (DbUpdateConcurrencyException → 409).
public sealed class OrderService(
    IAgriSageDbContext context,
    OrderBuilder builder,
    OrderQueries queries,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(CreateCounterOrderRequest request, CancellationToken cancellationToken)
    {
        EnumText.TryParse<CustomerType>(request.CustomerType, out var customerType);
        EnumText.TryParse<SettlementType>(request.SettlementType, out var settlementType);
        EnumText.TryParse<FulfillmentType>(request.FulfillmentType, out var fulfillmentType);

        var order = await builder.BuildAsync(
            new OrderDraft(
                OrderSource.Counter, customerType, settlementType, fulfillmentType, request.Items, request.FarmerProfileId,
                request.CustomerName, request.CustomerPhone, request.AddressId, request.DeliveryAddress, request.Note,
                AllowPriceOverride: true),
            cancellationToken);

        context.Orders.Add(order);
        builder.RecordPriceOverrides(order);
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(order.Id, cancellationToken);
    }

    public async Task<PagedResult<OrderListItem>> ListAsync(OrderListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.Orders.AsNoTracking().Where(o => o.StoreId == storeId);

        if (EnumText.TryParse<OrderStatus>(request.Status, out var status))
        {
            query = query.Where(o => o.Status == status);
        }

        if (EnumText.TryParse<CustomerType>(request.CustomerType, out var customerType))
        {
            query = query.Where(o => o.CustomerType == customerType);
        }

        if (EnumText.TryParse<SettlementType>(request.SettlementType, out var settlementType))
        {
            query = query.Where(o => o.SettlementType == settlementType);
        }

        if (EnumText.TryParse<FulfillmentType>(request.FulfillmentType, out var fulfillmentType))
        {
            query = query.Where(o => o.FulfillmentType == fulfillmentType);
        }

        if (EnumText.TryParse<OrderSource>(request.Source, out var source))
        {
            query = query.Where(o => o.Source == source);
        }

        if (request.FarmerProfileId is { } farmerId)
        {
            query = query.Where(o => o.FarmerProfileId == farmerId);
        }

        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(o => o.CreatedAt >= start);
        }

        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(o => o.CreatedAt < end);
        }

        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLower();
            query = query.Where(o => o.OrderNumber.ToLower().Contains(term)
                || o.CustomerNameSnapshot.ToLower().Contains(term)
                || (o.CustomerPhoneSnapshot != null && o.CustomerPhoneSnapshot.Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.OrderNumber)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Source, o.CustomerType, o.CustomerNameSnapshot, o.CustomerPhoneSnapshot,
                o.SettlementType, o.FulfillmentType, o.Status, o.TotalAmount, ItemCount = o.Items.Count(),
                o.CreatedAt, o.ConfirmedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(o => new OrderListItem(
            o.Id, o.OrderNumber, EnumText.Format(o.Source), EnumText.Format(o.CustomerType), o.CustomerNameSnapshot,
            o.CustomerPhoneSnapshot, EnumText.Format(o.SettlementType), EnumText.Format(o.FulfillmentType),
            EnumText.Format(o.Status), o.TotalAmount, o.ItemCount, o.CreatedAt, o.ConfirmedAt)).ToList();

        return new PagedResult<OrderListItem>(items, request.Page, request.PageSize, total);
    }

    public Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken) => queries.GetAsync(id, cancellationToken);

    public async Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);
        var before = new { order.Note, order.SourceAddressId, order.DeliveryAddressLine };

        if (request.Note is not null)
        {
            order.ChangeNote(Texts.Clean(request.Note));
        }

        if (request.AddressId is not null || request.DeliveryAddress is not null)
        {
            if (order.FulfillmentType != FulfillmentType.Delivery)
            {
                throw new BusinessRuleException("A PICKUP order has no delivery address.");
            }

            var customerUserId = order.FarmerProfileId is { } farmerId
                ? await context.FarmerProfiles.AsNoTracking().Where(f => f.Id == farmerId)
                    .Select(f => (Guid?)f.UserId).FirstOrDefaultAsync(cancellationToken)
                : null;
            var (address, sourceAddressId) = await builder.ResolveDeliveryAddressAsync(
                customerUserId, request.AddressId, request.DeliveryAddress, cancellationToken);
            order.UpdateDeliveryAddress(address, sourceAddressId);
        }

        audit.Record(
            "ORDER_UPDATED", "ORDER", order.Id, order.StoreId, before,
            new { order.Note, order.SourceAddressId, order.DeliveryAddressLine });
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> AddItemAsync(Guid id, OrderItemRequest request, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);

        if (order.Items.Any(i => !i.IsDeleted
            && i.StoreProductId == request.StoreProductId && i.ProductPackagingId == request.ProductPackagingId))
        {
            throw new BusinessRuleException("This product and packaging is already on the order; change the quantity of that line.");
        }

        // The line is priced by the order's own list, not by whatever list applies today.
        var priceListId = order.PriceListIdSnapshot
            ?? throw new BusinessRuleException("The order has no price list; it cannot take new lines.");
        var lines = await builder.ResolveLinesAsync(order.StoreId, priceListId, [request], true, cancellationToken);
        var item = OrderBuilder.AddLine(order, lines[0]);

        if (item.PriceOverridden)
        {
            builder.RecordPriceOverride(order, item);
        }

        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> ChangeItemQuantityAsync(
        Guid id, Guid itemId, ChangeOrderItemQuantityRequest request, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);
        var item = FindItem(order, itemId);
        var before = new { item.Quantity, item.LineTotalAmount };

        order.UpdateItemQuantity(itemId, request.Quantity);
        audit.Record(
            "ORDER_ITEM_QUANTITY_CHANGED", "ORDER", order.Id, order.StoreId,
            new { orderItemId = itemId, before.Quantity }, new { orderItemId = itemId, item.Quantity });
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> OverrideItemPriceAsync(
        Guid id, Guid itemId, OverrideOrderItemPriceRequest request, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);
        var item = FindItem(order, itemId);
        var actor = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var previous = item.UnitPrice;

        // The suggested price itself is not an override.
        order.OverrideItemPrice(
            itemId,
            request.UnitPrice == item.SuggestedUnitPrice ? null : new PriceOverride(request.UnitPrice, actor, request.Reason.Trim()));

        audit.Record(
            item.PriceOverridden ? "PRICE_OVERRIDE" : "PRICE_OVERRIDE_REMOVED",
            "ORDER",
            order.Id,
            order.StoreId,
            new { orderItemId = itemId, unitPrice = previous, item.SuggestedUnitPrice },
            new { orderItemId = itemId, item.UnitPrice },
            request.Reason.Trim());
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> RestoreItemPriceAsync(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);
        var item = FindItem(order, itemId);
        var previous = item.UnitPrice;

        order.OverrideItemPrice(itemId, null);
        audit.Record(
            "PRICE_OVERRIDE_REMOVED", "ORDER", order.Id, order.StoreId,
            new { orderItemId = itemId, unitPrice = previous }, new { orderItemId = itemId, item.UnitPrice });
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    public async Task<OrderResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken)
    {
        var order = await FindAsync(id, cancellationToken);
        var item = FindItem(order, itemId);

        order.RemoveItem(itemId, currentUser.UserId, clock.UtcNow);
        audit.Record(
            "ORDER_ITEM_REMOVED", "ORDER", order.Id, order.StoreId,
            new { orderItemId = itemId, sku = item.ProductSkuSnapshot, item.Quantity });
        await SaveAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    private async Task<Order> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        return await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", id);
    }

    private static OrderItem FindItem(Order order, Guid itemId) =>
        order.Items.FirstOrDefault(i => i.Id == itemId && !i.IsDeleted) ?? throw new NotFoundException("Order item", itemId);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two orders raced for the same number; the unique index decided.
            throw new ConflictException("Another order was created at the same time; try again.");
        }
    }
}
