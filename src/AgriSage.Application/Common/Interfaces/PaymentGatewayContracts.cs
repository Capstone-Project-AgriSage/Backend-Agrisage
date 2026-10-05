namespace AgriSage.Application.Common.Interfaces;

// Return/cancel URLs and the link lifetime are adapter configuration (PayOS:ReturnUrl, PayOS:CancelUrl,
// PayOS:LinkExpiryMinutes), not business data; the result says when the link expires.
public sealed record CreatePaymentLinkRequest(long OrderCode, long Amount, string Description);

// QrCode = the VietQR payload string (the client renders it as an image).
public sealed record PaymentLinkResult(string PaymentLinkId, string CheckoutUrl, string? QrCode, DateTimeOffset? ExpiresAt);

public enum PaymentLinkStatus
{
    Pending,
    Processing,
    Paid,
    Underpaid,
    Cancelled,
    Expired,
    Failed
}

public sealed record PaymentLinkState(long OrderCode, PaymentLinkStatus Status, long Amount, long AmountPaid);

// IsPaid = payOS `success` and data.code "00". RawData = the verified `data` object as JSON (kept in provider_metadata).
public sealed record PaymentWebhookData(long OrderCode, long Amount, bool IsPaid, string? Reference, string RawData);

public enum PaymentWebhookKind
{
    // No `data` or `signature` (registration test, health check): answer 200, change nothing.
    NoData,
    Invalid,
    Valid
}

public sealed record PaymentWebhookVerificationResult(PaymentWebhookKind Kind, PaymentWebhookData? Data);
