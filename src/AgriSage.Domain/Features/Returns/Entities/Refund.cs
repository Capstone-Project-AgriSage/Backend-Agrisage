using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Domain.Features.Returns.Entities;

// Money returned to the customer. AgriSage records the refund; staff execute it externally (no automatic payOS
// refund in MVP). PENDING → COMPLETED | FAILED | CANCELLED, all terminal.
// Exactly one source (database design §35.18):
// - a Sales Return: the already-paid part of returned goods; changed only through SalesReturn;
// - an Order cancelled in full or in part: order prepayment that was never consumed; changed only through Order,
//   always for one original payment. The refundable cap (unconsumed, reversed prepayment of that payment) is
//   checked by the cancellation use case.
public sealed class Refund : SoftDeletableChildEntity
{
    public const string DefaultCurrency = "VND";

    private Refund()
    {
    }

    internal Refund(
        Guid salesReturnId,
        Guid storeId,
        string refundNumber,
        RefundMethod refundMethod,
        decimal amount,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        Guid? originalPaymentId,
        string? note)
        : this(storeId, refundNumber, refundMethod, amount, requestedBy, requestedAt, originalPaymentId, note)
    {
        SalesReturnId = salesReturnId;
    }

    private Refund(
        Guid storeId,
        string refundNumber,
        RefundMethod refundMethod,
        decimal amount,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        Guid? originalPaymentId,
        string? note)
    {
        StoreId = storeId;
        RefundNumber = Guard.NotNullOrWhiteSpace(refundNumber);
        RefundMethod = refundMethod;
        Amount = Guard.PositiveMoney(amount);
        Currency = DefaultCurrency;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        OriginalPaymentId = originalPaymentId;
        Note = note;
        Status = RefundStatus.Pending;
    }

    public Guid StoreId { get; private set; }

    public string RefundNumber { get; private set; } = null!;

    // Set for a Sales Return refund; null for a cancelled-Order refund.
    public Guid? SalesReturnId { get; private set; }

    // Set for a cancelled-Order refund; null for a Sales Return refund.
    public Guid? OrderId { get; private set; }

    // Optional for a Sales Return refund (one return may involve several payments, or be refunded outside payOS);
    // required for a cancelled-Order refund.
    public Guid? OriginalPaymentId { get; private set; }

    public RefundMethod RefundMethod { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public RefundStatus Status { get; private set; }

    public string? ExternalReference { get; private set; }

    public string? ProofFileUrl { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? CompletedBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public string? Note { get; private set; }

    // Pending and completed refunds count toward the refundable total of their source.
    public bool CountsTowardRefundTotal => Status is RefundStatus.Pending or RefundStatus.Completed;

    // Refund of unconsumed prepayment of a cancelled or partially cancelled Order; created by Order.
    internal static Refund ForCancelledOrder(
        Guid storeId,
        string refundNumber,
        Guid orderId,
        Guid originalPaymentId,
        RefundMethod refundMethod,
        decimal amount,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        string? note = null)
    {
        if (orderId == Guid.Empty || originalPaymentId == Guid.Empty)
        {
            throw new DomainException("A cancelled-order refund needs its order and its original payment.");
        }

        return new Refund(storeId, refundNumber, refundMethod, amount, requestedBy, requestedAt, originalPaymentId, note)
        {
            OrderId = orderId
        };
    }

    internal void Complete(Guid completedBy, DateTimeOffset completedAt, string? externalReference, string? proofFileUrl)
    {
        EnsurePending();
        Status = RefundStatus.Completed;
        CompletedBy = completedBy;
        CompletedAt = completedAt;
        ExternalReference = externalReference;
        ProofFileUrl = proofFileUrl;
    }

    // FAILED changes the status only (no failed_at column); retry with a new Refund.
    internal void Fail()
    {
        EnsurePending();
        Status = RefundStatus.Failed;
    }

    internal void Cancel(Guid cancelledBy, DateTimeOffset cancelledAt, string? reason)
    {
        EnsurePending();
        Status = RefundStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = reason;
    }

    private void EnsurePending()
    {
        if (Status != RefundStatus.Pending)
        {
            throw new DomainException($"Refund '{RefundNumber}' is {Status}; only a PENDING refund can change.");
        }
    }
}
