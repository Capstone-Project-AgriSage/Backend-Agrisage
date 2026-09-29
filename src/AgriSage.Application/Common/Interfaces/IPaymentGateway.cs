namespace AgriSage.Application.Common.Interfaces;

// Online payment provider abstraction (payOS). The webhook is the source of truth for payment confirmation;
// callers must still verify amount/order code/state and apply it idempotently.
public interface IPaymentGateway
{
    Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken);

    PaymentWebhookVerificationResult VerifyWebhook(string rawPayload);
}
