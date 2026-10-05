using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Payments;

public interface IPaymentService
{
    Task<PaymentResponse> ReceiveCashAsync(CashPaymentRequest request, CancellationToken cancellationToken);

    Task<PagedResult<PaymentListItem>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken);

    Task<PaymentResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OrderPaymentSummary> GetOrderSummaryAsync(Guid orderId, CancellationToken cancellationToken);

    Task<PaymentResponse> CancelAsync(Guid id, CancelPaymentRequest request, CancellationToken cancellationToken);
}

// Cash payments and payment queries for staff (task F1.3, FLOW_1 §5). Receiving cash owns one transaction: lock the
// order → check → create the PAID payment → PaymentAllocator → audit → one SaveChanges → commit.
public sealed class PaymentService(
    IAgriSageDbContext context,
    IRowLockService locks,
    IOrderPrepaymentLedger ledger,
    PaymentAllocator allocator,
    PaymentQueries queries,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IPaymentService
{
    public async Task<PaymentResponse> ReceiveCashAsync(CashPaymentRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        EnumText.TryParse<PaymentContext>(request.PaymentContext, out var paymentContext);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var now = clock.UtcNow;

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        Payment payment;
        if (paymentContext == PaymentContext.OrderPayment)
        {
            var orderId = request.OrderId ?? throw new BusinessRuleException("An ORDER_PAYMENT needs an orderId.");
            await locks.LockOrderAsync(orderId, cancellationToken);

            var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
                ?? throw new NotFoundException("Order", orderId);

            if (order.Status is OrderStatus.Cancelled or OrderStatus.PartiallyCancelled or OrderStatus.Completed)
            {
                throw new BusinessRuleException(
                    $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; it cannot take a payment.");
            }

            var remaining = order.TotalAmount - await ledger.GetPaidAmountAsync(order.Id, cancellationToken);
            if (request.Amount > remaining)
            {
                throw new BusinessRuleException(
                    $"Order '{order.OrderNumber}' has {Math.Max(0m, remaining):0.##} left to pay; the payment is {request.Amount:0.##}.");
            }

            payment = await NewPaidPaymentAsync(storeId, PaymentContext.OrderPayment, request, order.FarmerProfileId, order.Id, actorId, now, cancellationToken);
            await allocator.AllocateAsync(payment, order, null, actorId, cancellationToken);
        }
        else
        {
            var farmerId = request.FarmerProfileId ?? throw new BusinessRuleException("A DEBT_REPAYMENT needs a farmerProfileId.");
            if (!await context.FarmerProfiles.AsNoTracking().AnyAsync(f => f.Id == farmerId, cancellationToken))
            {
                throw new NotFoundException("Farmer profile", farmerId);
            }

            var balance = await context.DebtAccounts.AsNoTracking()
                .Where(a => a.StoreId == storeId && a.FarmerProfileId == farmerId)
                .Select(a => a.CurrentBalance).FirstOrDefaultAsync(cancellationToken);
            if (request.Amount > balance)
            {
                throw new BusinessRuleException($"The customer owes {balance:0.##}; the payment is {request.Amount:0.##}.");
            }

            payment = await NewPaidPaymentAsync(storeId, PaymentContext.DebtRepayment, request, farmerId, null, actorId, now, cancellationToken);
            await allocator.AllocateAsync(
                payment, null, request.DebtAllocations?.Select(a => new RequestedDebtAllocation(a.DebtEntryId, a.Amount)).ToList(),
                actorId, cancellationToken);
        }

        audit.Record(
            "PAYMENT_RECEIVED", "PAYMENT", payment.Id, storeId, null,
            new { payment.PaymentNumber, paymentContext = EnumText.Format(payment.PaymentContext), method = "CASH", payment.Amount, payment.OrderId, farmerProfileId = payment.PayerFarmerProfileId });
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(payment.Id, null, cancellationToken);
    }

    public Task<PagedResult<PaymentListItem>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken) =>
        queries.ListAsync(request, null, cancellationToken);

    public Task<PaymentResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        queries.GetAsync(id, null, cancellationToken);

    public Task<OrderPaymentSummary> GetOrderSummaryAsync(Guid orderId, CancellationToken cancellationToken) =>
        queries.GetOrderSummaryAsync(orderId, null, cancellationToken);

    // Only a PENDING payment. A PENDING payOS payment is cancelled through payOS (F2.4), which also closes its link.
    public async Task<PaymentResponse> CancelAsync(Guid id, CancelPaymentRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockPaymentAsync(id, cancellationToken);

        var payment = await context.Payments.FirstOrDefaultAsync(p => p.Id == id && p.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Payment", id);

        if (payment.PaymentMethod == PaymentMethod.PayOs)
        {
            throw new BusinessRuleException(
                "A payOS payment is closed through payOS: the customer cancels it (POST /api/me/payments/{id}/cancel) or it expires; "
                + "update it with POST /api/payments/{id}/sync.");
        }

        payment.Cancel(clock.UtcNow);
        audit.Record(
            "PAYMENT_CANCELLED", "PAYMENT", payment.Id, storeId, null,
            new { payment.PaymentNumber }, Texts.Clean(request.Reason));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await queries.GetAsync(id, null, cancellationToken);
    }

    private Task<Payment> NewPaidPaymentAsync(
        Guid storeId,
        PaymentContext paymentContext,
        CashPaymentRequest request,
        Guid? payerFarmerProfileId,
        Guid? orderId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        CashPayments.CreatePaidAsync(
            context, storeId, paymentContext, request.Amount, payerFarmerProfileId, orderId, actorId, now, request.Note, cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two payments raced for the same number; the unique index decided.
            throw new ConflictException("Another payment was recorded at the same time; try again.");
        }
    }
}
