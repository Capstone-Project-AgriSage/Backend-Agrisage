using AgriSage.Application.Common;
using AgriSage.Domain.Features.Returns.Entities;

namespace AgriSage.Application.Features.Returns;

// Shared shape (api-flows README §2): the same for return refunds and cancelled-order refunds. Requested by L1
// (order cancellation, shown in OrderPaymentSummary); every refund route is owned by L4 (F4.5).
public sealed record RefundResponse(
    Guid Id,
    string RefundNumber,
    string Source,
    Guid? SalesReturnId,
    Guid? OrderId,
    Guid? OriginalPaymentId,
    string RefundMethod,
    decimal Amount,
    string Status,
    string? ExternalReference,
    string? ProofFileUrl,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? CompletedBy,
    DateTimeOffset? CompletedAt,
    Guid? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    string? Note)
{
    public static RefundResponse From(Refund refund) => new(
        refund.Id,
        refund.RefundNumber,
        refund.SalesReturnId is null ? "ORDER" : "SALES_RETURN",
        refund.SalesReturnId,
        refund.OrderId,
        refund.OriginalPaymentId,
        EnumText.Format(refund.RefundMethod),
        refund.Amount,
        EnumText.Format(refund.Status),
        refund.ExternalReference,
        refund.ProofFileUrl,
        refund.RequestedBy,
        refund.RequestedAt,
        refund.CompletedBy,
        refund.CompletedAt,
        refund.CancelledBy,
        refund.CancelledAt,
        refund.CancelReason,
        refund.Note);
}
