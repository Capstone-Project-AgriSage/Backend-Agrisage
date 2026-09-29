using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Domain.Features.Returns.Entities;

// Money returned to the customer for the already-paid part of a Sales Return. Changed only through SalesReturn.
// AgriSage records the refund; staff execute it externally (no automatic payOS refund in MVP).
// PENDING → COMPLETED | FAILED | CANCELLED, all terminal.
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
    {
        SalesReturnId = salesReturnId;
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

    public Guid SalesReturnId { get; private set; }

    // Optional: one return may involve several payments, or be refunded outside payOS.
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

    // Pending and completed refunds count toward the Sales Return's refund total.
    public bool CountsTowardRefundTotal => Status is RefundStatus.Pending or RefundStatus.Completed;

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
