using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Orders;

public sealed record CancelOrderRequest(string Reason);

// What staff must hand back for a cancelled order; the refunds are completed with the refund routes (task F4.5).
public sealed record OrderCancellationRefund(Guid RefundId, string RefundNumber, Guid PaymentId, string RefundMethod, decimal Amount);

public sealed record OrderCancellationResponse(OrderResponse Order, IReadOnlyList<OrderCancellationRefund> Refunds);

public interface IOrderCancellationService
{
    Task<OrderCancellationResponse> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken);
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}

// POST /api/orders/{id}/cancel (task F1.6, FLOW_1 §8): one transaction — lock the order → OrderCanceller → audit →
// one SaveChanges → commit. Only before anything was fulfilled; after a partial fulfillment use cancel-remaining.
public sealed class OrderCancellationService(
    IAgriSageDbContext context,
    IRowLockService locks,
    OrderCanceller canceller,
    OrderQueries queries,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors,
    AuditTrail audit) : IOrderCancellationService
{
    public async Task<OrderCancellationResponse> CancelAsync(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var reason = request.Reason.Trim();

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockOrderAsync(orderId, cancellationToken);

        var order = await context.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);

        if (order.Status is OrderStatus.PartiallyFulfilled)
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is already partly handed over; cancel the rest of its lines instead.");
        }

        if (order.Status is not (OrderStatus.PendingConfirmation or OrderStatus.Confirmed or OrderStatus.Preparing or OrderStatus.ReadyForFulfillment))
        {
            throw new BusinessRuleException(
                $"Order '{order.OrderNumber}' is {EnumText.Format(order.Status)}; it can no longer be cancelled.");
        }

        var previous = order.Status;
        var refunds = await canceller.CancelAsync(order, actorId, clock.UtcNow, reason, cancellationToken);

        audit.Record(
            "ORDER_CANCELLED",
            "ORDER",
            order.Id,
            order.StoreId,
            new { status = EnumText.Format(previous) },
            new { status = EnumText.Format(order.Status), refunds = refunds.Select(r => new { r.RefundNumber, r.Amount }) },
            reason);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Two refunds raced for the same number; the unique index decided.
            throw new ConflictException("Another refund was requested at the same time; try again.");
        }

        await transaction.CommitAsync(cancellationToken);

        return new OrderCancellationResponse(
            await queries.GetAsync(orderId, cancellationToken),
            refunds.Select(r => new OrderCancellationRefund(r.RefundId, r.RefundNumber, r.PaymentId, EnumText.Format(r.RefundMethod), r.Amount)).ToList());
    }
}
