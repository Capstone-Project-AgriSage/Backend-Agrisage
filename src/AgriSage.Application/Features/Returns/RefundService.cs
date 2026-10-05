using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Returns;

// Transaction owner for refund records only. Staff execute the money transfer outside the system.
// Lock order → return (when applicable) → payment; both refund sources share the payment's remaining capacity.
public sealed class RefundService(IAgriSageDbContext context, IRowLockService locks, ICurrentUserService user,
    IDateTimeProvider clock, AuditTrail audit) : IRefundService
{
    public Task<RefundResponse> CreateReturnAsync(Guid id, RefundRequest request, CancellationToken token) =>
        ReturnAsync(id, null, request.OriginalPaymentId, async (r, _, actor) =>
        {
            EnumText.TryParse<RefundMethod>(request.RefundMethod, out var method);
            var refund = r.RequestRefund(await NumberAsync(r.StoreId, token), method, request.Amount, actor, clock.UtcNow,
                request.OriginalPaymentId, Texts.Clean(request.Note), Texts.Clean(request.ExternalReference));
            return refund;
        }, token, request.Amount);

    public Task<RefundResponse> CompleteReturnAsync(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token) =>
        ReturnAsync(id, refundId, null, async (r, refund, actor) =>
        {
            r.CompleteRefund(refundId, actor, clock.UtcNow, Texts.Clean(request.ExternalReference), Texts.Clean(request.ProofFileUrl), Texts.Clean(request.Note));
            await UpdatePaymentAsync(refund!, token);
            if (r.Refunds.Where(f => !f.IsDeleted && f.Status == RefundStatus.Completed).Sum(f => f.Amount) == r.TotalRefundAmount)
                r.Complete(clock.UtcNow);
            return refund!;
        }, token);
    public Task<RefundResponse> FailReturnAsync(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token) =>
        ReturnAsync(id, refundId, null, (r, refund, _) =>
        {
            r.FailRefund(refundId, Texts.Clean(request.Note)); return Task.FromResult(refund!);
        }, token);
    public Task<RefundResponse> CancelReturnAsync(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token) =>
        ReturnAsync(id, refundId, null, (r, refund, actor) =>
        {
            r.CancelRefund(refundId, actor, clock.UtcNow, request.Reason.Trim()); return Task.FromResult(refund!);
        }, token);

    public async Task<IReadOnlyList<RefundResponse>> ListOrderAsync(Guid id, CancellationToken token)
    {
        Actor(); var storeId = await ActiveStore.GetIdAsync(context, token);
        var order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id && o.StoreId == storeId, token)
            ?? throw new NotFoundException("Order", id);
        EnsureCancelled(order);
        var rows = await context.Refunds.AsNoTracking().Where(r => r.OrderId == id && r.StoreId == storeId)
            .OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(token);
        return rows.Select(RefundResponse.From).ToList();
    }
    public Task<RefundResponse> CreateOrderAsync(Guid id, OrderRefundRequest request, CancellationToken token) =>
        OrderAsync(id, null, request.OriginalPaymentId, async (o, _, actor) =>
        {
            EnumText.TryParse<RefundMethod>(request.RefundMethod, out var method);
            return o.RequestCancellationRefund(await NumberAsync(o.StoreId, token), request.OriginalPaymentId,
                method, request.Amount, actor, clock.UtcNow, Texts.Clean(request.Note));
        }, token, request.Amount);
    public Task<RefundResponse> CompleteOrderAsync(Guid id, Guid refundId, CompleteRefundRequest request, CancellationToken token) =>
        OrderAsync(id, refundId, null, async (o, refund, actor) =>
        {
            o.CompleteCancellationRefund(refundId, actor, clock.UtcNow, Texts.Clean(request.ExternalReference), Texts.Clean(request.ProofFileUrl), Texts.Clean(request.Note));
            await UpdatePaymentAsync(refund!, token); return refund!;
        }, token);
    public Task<RefundResponse> FailOrderAsync(Guid id, Guid refundId, FailRefundRequest request, CancellationToken token) =>
        OrderAsync(id, refundId, null, (o, refund, _) =>
        {
            o.FailCancellationRefund(refundId, Texts.Clean(request.Note)); return Task.FromResult(refund!);
        }, token);
    public Task<RefundResponse> CancelOrderAsync(Guid id, Guid refundId, CancelRefundRequest request, CancellationToken token) =>
        OrderAsync(id, refundId, null, (o, refund, actor) =>
        {
            o.CancelCancellationRefund(refundId, actor, clock.UtcNow, request.Reason.Trim()); return Task.FromResult(refund!);
        }, token);

    private async Task<RefundResponse> ReturnAsync(Guid id, Guid? refundId, Guid? requestedPaymentId,
        Func<SalesReturn, Refund?, Guid, Task<Refund>> change, CancellationToken token, decimal? requestedAmount = null)
    {
        var actor = Actor(); var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        var orderId = await context.SalesReturns.AsNoTracking().Where(r => r.Id == id && r.StoreId == storeId)
            .Select(r => (Guid?)r.OrderId).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Sales return", id);
        await locks.LockOrderAsync(orderId, token); await locks.LockSalesReturnAsync(id, token);
        var r = await context.SalesReturns.Include(r => r.Items).Include(r => r.Refunds)
            .SingleOrDefaultAsync(r => r.Id == id && r.StoreId == storeId, token) ?? throw new NotFoundException("Sales return", id);
        var existing = refundId is { } rid ? r.Refunds.SingleOrDefault(f => !f.IsDeleted && f.Id == rid)
            ?? throw new NotFoundException("Refund", rid) : null;
        var paymentId = existing?.OriginalPaymentId ?? requestedPaymentId;
        if (paymentId is { } pid)
        {
            var payment = await PaymentAsync(pid, orderId, storeId, token);
            if (requestedAmount is { } amount) await CheckPaymentCapacityAsync(payment, amount, token);
        }
        var result = await change(r, existing, actor);
        return await SaveAsync(result, transaction, token);
    }

    private async Task<RefundResponse> OrderAsync(Guid id, Guid? refundId, Guid? requestedPaymentId,
        Func<Order, Refund?, Guid, Task<Refund>> change, CancellationToken token, decimal? requestedAmount = null)
    {
        var actor = Actor(); var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token); await locks.LockOrderAsync(id, token);
        var order = await context.Orders.Include(o => o.CancellationRefunds).SingleOrDefaultAsync(o => o.Id == id && o.StoreId == storeId, token)
            ?? throw new NotFoundException("Order", id);
        EnsureCancelled(order);
        var existing = refundId is { } rid ? order.CancellationRefunds.SingleOrDefault(f => !f.IsDeleted && f.Id == rid)
            ?? throw new NotFoundException("Refund", rid) : null;
        var paymentId = existing?.OriginalPaymentId ?? requestedPaymentId ?? throw new BusinessRuleException("An original payment is required.");
        var payment = await PaymentAsync(paymentId, id, storeId, token);
        if (requestedAmount is { } amount)
        {
            var cap = await CancellationCapAsync(payment, id, token);
            var committed = await context.Refunds.AsNoTracking().Where(f => f.OrderId == id && f.OriginalPaymentId == paymentId
                && (f.Status == RefundStatus.Pending || f.Status == RefundStatus.Completed)).SumAsync(f => f.Amount, token);
            if (amount > cap - committed) throw new BusinessRuleException("The refund exceeds this payment's amount released by order cancellation.");
            await CheckPaymentCapacityAsync(payment, amount, token);
        }
        var result = await change(order, existing, actor);
        return await SaveAsync(result, transaction, token);
    }

    private async Task<Payment> PaymentAsync(Guid id, Guid orderId, Guid storeId, CancellationToken token)
    {
        await locks.LockPaymentAsync(id, token);
        var payment = await context.Payments.SingleOrDefaultAsync(p => p.Id == id && p.StoreId == storeId && p.OrderId == orderId, token)
            ?? throw new NotFoundException("Original order payment", id);
        if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            throw new BusinessRuleException("Only a paid payment can be refunded.");
        return payment;
    }
    private async Task CheckPaymentCapacityAsync(Payment payment, decimal amount, CancellationToken token)
    {
        var committed = await context.Refunds.AsNoTracking().Where(f => f.OriginalPaymentId == payment.Id
            && (f.Status == RefundStatus.Pending || f.Status == RefundStatus.Completed)).SumAsync(f => f.Amount, token);
        if (amount > payment.Amount - committed) throw new BusinessRuleException("The refund exceeds the original payment's remaining refundable amount.");
    }
    private async Task UpdatePaymentAsync(Refund refund, CancellationToken token)
    {
        if (refund.OriginalPaymentId is not { } id) return;
        // The current refund is still PENDING in the database until our single SaveChanges.
        var completed = await context.Refunds.AsNoTracking().Where(f => f.OriginalPaymentId == id && f.Status == RefundStatus.Completed)
            .SumAsync(f => f.Amount, token);
        var payment = context.Payments.Local.Single(p => p.Id == id);
        payment.RecordCompletedRefundTotal(completed + refund.Amount);
    }
    private async Task<decimal> CancellationCapAsync(Payment payment, Guid orderId, CancellationToken token)
    {
        // Partial cancellation shrinks an active allocation (§35.21). Its immutable release audit is the source
        // of that amount; failed/cancelled retry refunds must never increase the cap by being summed again.
        var logs = await context.AuditLogs.AsNoTracking().Where(a => a.StoreId == payment.StoreId && a.EntityId == payment.Id
            && a.EntityType == "PAYMENT" && a.Action == "ORDER_PREPAYMENT_RELEASED" && a.NewValues != null)
            .Select(a => a.NewValues!).ToListAsync(token);
        decimal released = 0; var found = false;
        foreach (var json in logs)
        {
            using var document = JsonDocument.Parse(json); var value = document.RootElement;
            if (value.TryGetProperty("orderId", out var order) && order.TryGetGuid(out var loggedOrder) && loggedOrder == orderId
                && value.TryGetProperty("released", out var amount) && amount.TryGetDecimal(out var releasedAmount))
            { released += releasedAmount; found = true; }
        }
        if (found) return released;
        // Older full reversals are still traceable directly to their allocation rows.
        return await context.PaymentAllocations.AsNoTracking().Where(a => a.PaymentId == payment.Id && a.OrderId == orderId
            && a.Status == PaymentAllocationStatus.Reversed && a.PrepaymentConsumedAmount == 0).SumAsync(a => a.AllocatedAmount, token);
    }
    private Task<string> NumberAsync(Guid storeId, CancellationToken token) => DocumentNumbers.NextAsync(
        context.Refunds.IgnoreQueryFilters().AsNoTracking().Where(r => r.StoreId == storeId).Select(r => r.RefundNumber),
        DocumentNumbers.Refund, BusinessCalendar.Today(clock.UtcNow), token);
    private async Task<RefundResponse> SaveAsync(Refund refund, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken token)
    {
        audit.Record($"REFUND_{EnumText.Format(refund.Status)}", "REFUND", refund.Id, refund.StoreId,
            newValues: new { refund.OrderId, refund.SalesReturnId, refund.OriginalPaymentId, refund.Amount, refund.Note }, reason: refund.CancelReason);
        await context.SaveChangesAsync(token); await transaction.CommitAsync(token); return RefundResponse.From(refund);
    }
    private Guid Actor()
    {
        var actor = user.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (user.Role is not ("ADMIN" or "STORE_OWNER")) throw new ForbiddenException(); return actor;
    }
    private static void EnsureCancelled(Order order)
    {
        if (order.Status is not (OrderStatus.Cancelled or OrderStatus.PartiallyCancelled))
            throw new BusinessRuleException("Refunds of unused prepayment require a CANCELLED or PARTIALLY_CANCELLED order.");
    }
}
