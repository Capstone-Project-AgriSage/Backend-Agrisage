using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// FLOW_2 §5 (task F2.3). Reuses L1's shared steps: OrderBuilder (checkout, no price overrides), OrderQueries and
// OrderCanceller. A Farmer only sees and cancels their own orders; someone else's id is "not found", never 403.
public sealed class MeOrderService(
    IAgriSageDbContext context,
    CurrentFarmer currentFarmer,
    OrderBuilder builder,
    OrderQueries queries,
    IOrderService orders,
    OrderCanceller canceller,
    IRowLockService locks,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IMeOrderService
{
    public const string DefaultCancelReason = "Cancelled by the customer.";

    // One transaction: the order from the ACTIVE cart's lines, and the cart CONVERTED with the order id.
    public async Task<OrderResponse> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        EnumText.TryParse<OrderSource>(request.Source, out var source);
        EnumText.TryParse<SettlementType>(request.SettlementType, out var settlementType);
        EnumText.TryParse<FulfillmentType>(request.FulfillmentType, out var fulfillmentType);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        // Same lock as every cart change, so the cart cannot change while it is being converted.
        await locks.LockFarmerProfileAsync(farmer.FarmerProfileId, cancellationToken);

        var cart = await context.Carts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.FarmerProfileId == farmer.FarmerProfileId
                && c.Status == CartStatus.Active, cancellationToken);
        var lines = cart?.Items.Where(i => !i.IsDeleted).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new OrderItemRequest(i.StoreProductId, i.ProductPackagingId, i.Quantity)).ToList() ?? [];
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("The cart is empty.");
        }

        if (lines.Count > CreateCounterOrderRequestValidator.MaxItems)
        {
            throw new BusinessRuleException($"An order can have at most {CreateCounterOrderRequestValidator.MaxItems} lines.");
        }

        // A line that can no longer be ordered (not sellable, no price) fails with a per-line 422 until it is removed.
        var order = await builder.BuildAsync(
            new OrderDraft(
                source, CustomerType.Registered, settlementType, fulfillmentType, lines, farmer.FarmerProfileId,
                AddressId: request.AddressId, DeliveryAddress: request.DeliveryAddress, Note: request.Note,
                AllowPriceOverride: false),
            cancellationToken);

        context.Orders.Add(order);
        cart!.MarkConverted(order.Id, clock.UtcNow);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two orders raced for the same number; the unique index decided.
            throw new ConflictException("Another order was created at the same time; try again.");
        }

        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(order.Id, cancellationToken);
    }

    public async Task<PagedResult<OrderListItem>> ListAsync(MyOrderListRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);

        // The Farmer filter always comes from the JWT.
        return await orders.ListAsync(
            new OrderListRequest
            {
                Status = request.Status, FromDate = request.FromDate, ToDate = request.ToDate,
                FarmerProfileId = farmer.FarmerProfileId, Page = request.Page, PageSize = request.PageSize
            },
            cancellationToken);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureOwnAsync(id, cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    // Only while PENDING_CONFIRMATION. OrderCanceller cancels a PENDING payOS link and requests a refund for a PAID payment.
    public async Task<OrderResponse> CancelAsync(Guid id, CancelMyOrderRequest request, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var reason = Texts.Clean(request.Reason) ?? DefaultCancelReason;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(id, cancellationToken);

        var order = await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.StoreId == storeId && o.FarmerProfileId == farmer.FarmerProfileId, cancellationToken)
            ?? throw new NotFoundException("Order", id);
        if (order.Status != OrderStatus.PendingConfirmation)
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; only an order waiting for confirmation can be cancelled online. Contact the store.");
        }

        var refunds = await canceller.CancelAsync(order, farmer.UserId, clock.UtcNow, reason, cancellationToken);
        audit.Record(
            "ORDER_CANCELLED",
            "ORDER",
            order.Id,
            order.StoreId,
            new { status = EnumText.Format(OrderStatus.PendingConfirmation) },
            new { status = EnumText.Format(order.Status), refunds = refunds.Select(r => new { r.RefundNumber, r.Amount }) },
            reason);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            throw new ConflictException("Another refund was requested at the same time; try again.");
        }

        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, cancellationToken);
    }

    private async Task EnsureOwnAsync(Guid id, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        if (!await context.Orders.AsNoTracking()
                .AnyAsync(o => o.Id == id && o.StoreId == storeId && o.FarmerProfileId == farmer.FarmerProfileId, cancellationToken))
        {
            throw new NotFoundException("Order", id);
        }
    }
}
