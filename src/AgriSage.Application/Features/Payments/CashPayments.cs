using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

// Shared step (task F1.3, reused by the quick counter sale F1.7): a cash payment received by `actorId`, created PAID with its
// PM- number and added to the caller's unit of work. It never saves; PaymentAllocator allocates it next.
public static class CashPayments
{
    public static async Task<Payment> CreatePaidAsync(
        IAgriSageDbContext context,
        Guid storeId,
        PaymentContext paymentContext,
        decimal amount,
        Guid? payerFarmerProfileId,
        Guid? orderId,
        Guid actorId,
        DateTimeOffset now,
        string? note,
        CancellationToken cancellationToken)
    {
        var number = await DocumentNumbers.NextAsync(
            context.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.StoreId == storeId).Select(p => p.PaymentNumber),
            DocumentNumbers.Payment,
            BusinessCalendar.Today(now),
            cancellationToken);

        var payment = new Payment(
            storeId, number, paymentContext, PaymentMethod.Cash, amount, now, payerFarmerProfileId, actorId,
            Texts.Clean(note), orderId: orderId);
        payment.MarkPaid(PaymentConfirmationSource.Staff, now, actorId);
        context.Payments.Add(payment);

        return payment;
    }
}
