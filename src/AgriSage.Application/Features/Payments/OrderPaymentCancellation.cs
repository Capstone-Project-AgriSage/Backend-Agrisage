using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

// Real IOrderPaymentCancellation (task F1.6, FLOW_1 §8; database design §35.18 and §35.21). Called when an order ended
// CANCELLED or PARTIALLY_CANCELLED; runs inside the caller's transaction and never saves.
//  - PENDING payments are cancelled; a PENDING payOS payment's link is closed at payOS first (task F2.4). This one gateway
//    call runs inside the caller's transaction, so the order cannot change meanwhile.
//  - The money that was paid for this order and will never be used goes back: every PAID payment gives up its unconsumed
//    ORDER prepayment (Payment.ReleaseUnconsumedPrepayment) and one PENDING cancelled-order refund is requested per payment
//    (CASH for cash, BANK_TRANSFER for payOS). Staff hand the money back and record it (no automatic payOS refund).
// What is refundable = paid − max(prepayment consumed, value of what was fulfilled), never below 0. The fulfilled value
// is counted too because the consumption of prepayment is posted with the debt (task F3.4); once that exists the two agree.
// The newest payments give back first, so the oldest keep covering what was delivered (consumption is oldest first).
public sealed class OrderPaymentCancellation(
    IAgriSageDbContext context,
    IRowLockService locks,
    IPaymentGateway gateway,
    IDateTimeProvider clock,
    AuditTrail audit) : IOrderPaymentCancellation
{
    public async Task<IReadOnlyList<CancellationRefundInfo>> ReverseForCancelledOrderAsync(
        Order order, Guid actorId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Locked in id order, then read: a webhook or a cancel of the same payment waits for this transaction.
        var paymentIds = await context.Payments.AsNoTracking().Where(p => p.OrderId == order.Id)
            .OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(cancellationToken);
        foreach (var id in paymentIds)
        {
            await locks.LockPaymentAsync(id, cancellationToken);
        }

        var payments = await context.Payments.Include(p => p.Allocations)
            .Where(p => paymentIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var pending in payments.Where(p => p.Status == PaymentStatus.Pending).OrderBy(p => p.Id))
        {
            if (pending.PaymentMethod == PaymentMethod.PayOs && pending.ProviderOrderCode is { } orderCode)
            {
                // F2.4: the link is closed at payOS before the payment is cancelled here. A link payOS already reports
                // PAID is not cancelled: the money arrived, so it is applied first (sync) and the cancel is retried.
                var link = await gateway.GetPaymentLinkAsync(orderCode, cancellationToken);
                if (link.Status == PaymentLinkStatus.Paid)
                {
                    throw new BusinessRuleException(
                        $"Payment '{pending.PaymentNumber}' was just paid online: sync it (POST /api/payments/{pending.Id}/sync), then cancel again.");
                }

                if (link.Status is PaymentLinkStatus.Pending or PaymentLinkStatus.Processing or PaymentLinkStatus.Underpaid)
                {
                    await gateway.CancelPaymentLinkAsync(orderCode, reason, cancellationToken);
                }
            }

            pending.Cancel(now);
            audit.Record("PAYMENT_CANCELLED", "PAYMENT", pending.Id, order.StoreId, null, new { pending.PaymentNumber, orderId = order.Id }, reason);
        }

        var allocations = payments
            .Where(p => p.Status == PaymentStatus.Paid && !p.IsDeleted)
            .SelectMany(p => p.Allocations
                .Where(a => !a.IsDeleted && a.IsActive && a.AllocationType == PaymentAllocationType.Order && a.OrderId == order.Id)
                .Select(a => (Payment: p, Allocation: a)))
            .ToList();

        var paid = allocations.Sum(x => x.Allocation.AllocatedAmount);
        var consumed = allocations.Sum(x => x.Allocation.PrepaymentConsumedAmount);
        var refundable = Math.Max(0m, paid - Math.Max(consumed, FulfilledValue(order)));
        if (refundable == 0m)
        {
            return [];
        }

        // Newest first, never more than an allocation still holds unconsumed.
        var released = new Dictionary<Guid, decimal>();
        foreach (var (payment, allocation) in allocations
                     .OrderByDescending(x => x.Allocation.AllocatedAt).ThenByDescending(x => x.Allocation.Id))
        {
            var take = Math.Min(refundable, allocation.AvailablePrepayment);
            if (take <= 0)
            {
                continue;
            }

            payment.ReleaseUnconsumedPrepayment(allocation.Id, take, actorId, now, reason);
            released[payment.Id] = released.GetValueOrDefault(payment.Id) + take;
            refundable -= take;
        }

        var storeId = order.StoreId;
        var day = BusinessCalendar.Today(now);
        var first = await DocumentNumbers.NextAsync(
            context.Refunds.IgnoreQueryFilters().AsNoTracking().Where(r => r.StoreId == storeId).Select(r => r.RefundNumber),
            DocumentNumbers.Refund,
            day,
            cancellationToken);
        var sequence = DocumentNumbers.SequenceOf(first, DocumentNumbers.Refund, day);

        var refunds = new List<CancellationRefundInfo>();
        foreach (var payment in payments.Where(p => released.ContainsKey(p.Id)).OrderBy(p => p.InitiatedAt).ThenBy(p => p.Id))
        {
            var amount = released[payment.Id];
            var method = payment.PaymentMethod == PaymentMethod.Cash ? RefundMethod.Cash : RefundMethod.BankTransfer;
            var refund = order.RequestCancellationRefund(
                DocumentNumbers.Format(DocumentNumbers.Refund, day, sequence++), payment.Id, method, amount, actorId, now, reason);

            refunds.Add(new CancellationRefundInfo(refund.Id, refund.RefundNumber, payment.Id, method, amount));
            audit.Record(
                "ORDER_PREPAYMENT_RELEASED", "PAYMENT", payment.Id, storeId, null,
                new { orderId = order.Id, released = amount, refundNumber = refund.RefundNumber, method = EnumText.Format(method) }, reason);
        }

        return refunds;
    }

    // Value of what was handed over: whole base units × price ÷ conversion per item (the order's own price snapshot).
    private static decimal FulfilledValue(Order order) =>
        order.Items.Where(i => !i.IsDeleted)
            .Sum(i => CostRounding.RoundMoney(i.FulfilledBaseQuantity * i.UnitPrice / i.ConversionToBaseSnapshot));
}
