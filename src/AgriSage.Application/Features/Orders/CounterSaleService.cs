using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

// Quick counter sale (task F1.7, FLOW_1 §9): a customer pays cash and takes the goods now. The same steps as the single
// endpoints, in ONE transaction and one SaveChanges, so any failure leaves no order, payment or stock change behind:
//   OrderBuilder → cash payment PAID for the total + PaymentAllocator → OrderConfirmer core with the lots the staff chose →
//   FulfillmentPostingService (also consumes the prepayment) → the order is COMPLETED.
// Each step hands entities (not ids) to the next: nothing is saved before the end, so the allocator, the reservation and the
// ledger read what was just added through the tracked entities (api-flows README §3.2).
public sealed class CounterSaleService(
    IAgriSageDbContext context,
    OrderBuilder builder,
    PaymentAllocator allocator,
    OrderConfirmer confirmer,
    FulfillmentPostingService posting,
    OrderQueries orderQueries,
    PaymentQueries paymentQueries,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : ICounterSaleService
{
    // Nothing is added to the context or saved: the order is only built to price it and FEFO proposes the lots.
    public async Task<CounterSalePreviewResponse> PreviewAsync(CounterSaleRequest request, CancellationToken cancellationToken)
    {
        var order = await builder.BuildAsync(DraftOf(request), cancellationToken);
        var items = order.Items.ToList();
        var proposals = await FefoProposals.ProposeAsync(
            context,
            BusinessCalendar.Today(clock.UtcNow),
            items.Select(i => new FefoDemand(i.Id, i.StoreProductId, i.BaseQuantity)).ToList(),
            cancellationToken);

        return new CounterSalePreviewResponse(
            order.CustomerGroupIdSnapshot,
            order.PriceListIdSnapshot,
            order.TotalAmount,
            items.Select((item, index) => new CounterSalePreviewItem(
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
                proposals[index].Lots,
                proposals[index].ShortageBaseQuantity)).ToList());
    }

    public async Task<CounterSaleResponse> SellAsync(CounterSaleRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var now = clock.UtcNow;

        var missing = request.Items
            .Select((item, index) => (item, index))
            .Where(x => x.item.Lots is not { Count: > 0 })
            .ToDictionary(x => $"items[{x.index}].lots", _ => new[] { "The lots handed over are required." });
        if (missing.Count > 0)
        {
            throw new ValidationException(missing);
        }

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        var order = await builder.BuildAsync(DraftOf(request), cancellationToken);
        if (order.TotalAmount <= 0m)
        {
            throw new BusinessRuleException("A counter sale must have a total above 0.");
        }

        context.Orders.Add(order);
        builder.RecordPriceOverrides(order);

        // The built order keeps the request's line order, so line i of the request is order item i.
        var items = order.Items.ToList();
        var picks = items
            .SelectMany((item, index) => request.Items[index].Lots!.Select(l => new LotPick(item.Id, l.InventoryLotId, l.BaseQuantity)))
            .ToList();

        var payment = await CashPayments.CreatePaidAsync(
            context, order.StoreId, PaymentContext.OrderPayment, order.TotalAmount, order.FarmerProfileId, order.Id, actorId, now, null,
            cancellationToken);
        await allocator.AllocateAsync(payment, order, null, actorId, cancellationToken);

        await confirmer.ConfirmCoreAsync(order, picks, cancellationToken);
        var result = await posting.PostAsync(
            order,
            picks.Select(p => new FulfillmentLine(p.OrderItemId, p.InventoryLotId, p.BaseQuantity)).ToList(),
            FulfillmentSource.Pickup,
            null,
            null,
            actorId,
            now,
            cancellationToken);

        if (order.Status != OrderStatus.Completed)
        {
            throw new BusinessRuleException($"The sale did not complete the order (it is {EnumText.Format(order.Status)}).");
        }

        audit.Record(
            "PAYMENT_RECEIVED", "PAYMENT", payment.Id, order.StoreId, null,
            new { payment.PaymentNumber, paymentContext = "ORDER_PAYMENT", method = "CASH", payment.Amount, payment.OrderId, farmerProfileId = payment.PayerFarmerProfileId });
        audit.Record(
            "COUNTER_SALE_COMPLETED",
            "ORDER",
            order.Id,
            order.StoreId,
            null,
            new { order.OrderNumber, payment = payment.PaymentNumber, movement = result.Movement.MovementNumber, order.TotalAmount },
            Texts.Clean(request.Note));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Another sale took the same order or payment number at the same moment; the unique index decided.
            throw new ConflictException("Another sale was recorded at the same time; try again.");
        }

        await transaction.CommitAsync(cancellationToken);

        return new CounterSaleResponse(
            await orderQueries.GetAsync(order.Id, cancellationToken),
            await paymentQueries.GetAsync(payment.Id, null, cancellationToken));
    }

    private static OrderDraft DraftOf(CounterSaleRequest request)
    {
        EnumText.TryParse<CustomerType>(request.CustomerType, out var customerType);

        return new OrderDraft(
            OrderSource.Counter,
            customerType,
            SettlementType.FullPayment,
            FulfillmentType.Pickup,
            request.Items.Select(i => new OrderItemRequest(i.StoreProductId, i.ProductPackagingId, i.Quantity, i.UnitPrice, i.OverrideReason)).ToList(),
            request.FarmerProfileId,
            request.CustomerName,
            request.CustomerPhone,
            Note: request.Note,
            AllowPriceOverride: true);
    }
}
