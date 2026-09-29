namespace AgriSage.Application.Common.Interfaces;

public sealed record CreatePaymentLinkRequest(
    long OrderCode,
    decimal Amount,
    string Description,
    string ReturnUrl,
    string CancelUrl);

public sealed record PaymentLinkResult(string PaymentLinkId, string CheckoutUrl);

public sealed record PaymentWebhookData(
    long OrderCode,
    decimal Amount,
    bool IsSuccess,
    string? ProviderTransactionId,
    DateTimeOffset? TransactionTime);

public sealed record PaymentWebhookVerificationResult(bool IsValid, PaymentWebhookData? Data);
