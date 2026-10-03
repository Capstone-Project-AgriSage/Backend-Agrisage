using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.Application.Features.Payments;

// Shared step (api-flows README §3.2 / §4.9; task F1.3): a payment just became PAID — allocate all of it. It works on
// tracked entities inside the caller's transaction and never saves. Used by cash payments, the payOS webhook (F2.4) and
// the quick counter sale (F1.7).
//   ORDER_PAYMENT  → one ORDER allocation to the payment's own order; for an already confirmed CREDIT order the same
//                    amount of its unused credit reservation is released (database design §XVI-D).
//   DEBT_REPAYMENT → IDebtRepaymentPosting creates the DEBT allocations and the PAYMENT debt transactions.
public sealed class PaymentAllocator(
    IDateTimeProvider clock,
    ICreditReservationAdjuster creditReservations,
    IDebtRepaymentPosting debtRepayments)
{
    public async Task AllocateAsync(
        Payment payment,
        Order? order,
        IReadOnlyList<RequestedDebtAllocation>? debtAllocations,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentStatus.Paid)
        {
            throw new BusinessRuleException($"Payment '{payment.PaymentNumber}' is not paid yet; it cannot be allocated.");
        }

        if (payment.PaymentContext == PaymentContext.OrderPayment)
        {
            var target = order ?? throw new ArgumentNullException(nameof(order), "An ORDER_PAYMENT is allocated to its order.");
            var amount = payment.UnallocatedAmount;

            payment.AllocateToOrder(target.Id, amount, clock.UtcNow, actorId);

            if (target.SettlementType == SettlementType.Credit && target.Status is
                OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForFulfillment or OrderStatus.PartiallyFulfilled)
            {
                await creditReservations.OnOrderPrepaymentAsync(target.Id, amount, actorId, cancellationToken);
            }

            return;
        }

        await debtRepayments.ApplyAsync(payment, debtAllocations, actorId, cancellationToken);

        if (payment.UnallocatedAmount != 0m)
        {
            throw new BusinessRuleException(
                $"The whole cash amount must be applied to debts; {payment.UnallocatedAmount:0.##} is left over.");
        }
    }
}
