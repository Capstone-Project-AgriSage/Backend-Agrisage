using AgriSage.Application.Common;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Returns;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.Application.Features.Payments;

// FLOW_1 §5 and api-flows README §2 (PaymentResponse is shared with L2 and L3).
public sealed record DebtAllocationInput(Guid DebtEntryId, decimal Amount);

// PaymentContext: ORDER_PAYMENT (orderId required) or DEBT_REPAYMENT (farmerProfileId required). Cash is received by the
// caller, so the payment is created PAID and fully allocated in one step.
public sealed record CashPaymentRequest(
    string PaymentContext,
    decimal Amount,
    Guid? OrderId = null,
    Guid? FarmerProfileId = null,
    IReadOnlyList<DebtAllocationInput>? DebtAllocations = null,
    string? Note = null);

public sealed record CancelPaymentRequest(string? Reason = null);

// Search: payment number. FromDate/ToDate: Vietnam days on initiatedAt.
public sealed record PaymentListRequest : PaginationRequest
{
    public string? PaymentContext { get; init; }

    public string? PaymentMethod { get; init; }

    public string? Status { get; init; }

    public Guid? OrderId { get; init; }

    public Guid? FarmerProfileId { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? Search { get; init; }
}

public sealed record MyPaymentListRequest : PaginationRequest
{
    public string? Status { get; init; }
}

public sealed record PaymentAllocationResponse(
    Guid Id,
    string AllocationType,
    Guid? OrderId,
    string? OrderNumber,
    Guid? DebtEntryId,
    string? EntryNumber,
    decimal AllocatedAmount,
    decimal PrepaymentConsumedAmount,
    string Status,
    DateTimeOffset AllocatedAt);

public sealed record PaymentResponse(
    Guid Id,
    string PaymentNumber,
    string PaymentContext,
    string PaymentMethod,
    decimal Amount,
    string Currency,
    string Status,
    Guid? PayerFarmerProfileId,
    string? PayerName,
    string? ConfirmationSource,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt,
    string? CheckoutUrl,
    long? ProviderOrderCode,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? FailedAt,
    DateTimeOffset? CancelledAt,
    string? Note,
    decimal UnallocatedAmount,
    IReadOnlyList<PaymentAllocationResponse> Allocations, string? Reference = null);

public sealed record PaymentListItem(
    Guid Id,
    string PaymentNumber,
    string PaymentContext,
    string PaymentMethod,
    decimal Amount,
    string Status,
    string? PayerName,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset InitiatedAt);

// RemainingToPay = order total − paid (never below 0). Refunds = refunds of the order's cancellation (managed by F4.5).
public sealed record OrderPaymentSummary(
    Guid OrderId,
    decimal OrderTotal,
    decimal PaidAmount,
    decimal AvailablePrepayment,
    decimal ConsumedPrepayment,
    decimal RemainingToPay,
    IReadOnlyList<PaymentListItem> Payments,
    IReadOnlyList<RefundResponse> Refunds);

// payment_method / confirmation_source as stored (PAYOS, PAYOS_WEBHOOK), which EnumText would split as PAY_OS.
public static class PaymentText
{
    public static string Format(PaymentMethod method) => method == PaymentMethod.PayOs ? "PAYOS" : EnumText.Format(method);

    public static string Format(PaymentConfirmationSource source) =>
        source == PaymentConfirmationSource.PayOsWebhook ? "PAYOS_WEBHOOK" : EnumText.Format(source);

    public static bool TryParseMethod(string? text, out PaymentMethod method)
    {
        switch (text?.Trim().ToUpperInvariant())
        {
            case "CASH":
                method = PaymentMethod.Cash;
                return true;
            case "PAYOS":
                method = PaymentMethod.PayOs;
                return true;
            case "BANK_TRANSFER":
                method = PaymentMethod.BankTransfer;
                return true;
            default:
                method = default;
                return false;
        }
    }
}
