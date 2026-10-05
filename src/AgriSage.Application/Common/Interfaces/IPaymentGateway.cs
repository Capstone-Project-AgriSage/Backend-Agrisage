namespace AgriSage.Application.Common.Interfaces;

// Online payment provider abstraction (payOS, FLOW_2 §6). The webhook is the source of truth for payment confirmation;
// callers must still verify amount/order code/state and apply it idempotently. Amounts are whole VND (payOS uses long).
// Not configured, unreachable or refused → PaymentGatewayUnavailableException (503, no provider details).
public interface IPaymentGateway
{
    Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken);

    // The SDK verification is asynchronous; never block on it.
    Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken);

    Task<PaymentLinkState> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken);

    Task<PaymentLinkState> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken);
}
