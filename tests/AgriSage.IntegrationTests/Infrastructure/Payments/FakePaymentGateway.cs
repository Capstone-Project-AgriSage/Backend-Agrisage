using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;

namespace AgriSage.IntegrationTests.Infrastructure.Payments;

// In-memory payOS for tests (FLOW_2 §10: the adapter is tested with a fake gateway; real payOS only opt-in).
// Links start PENDING; tests move them with SetStatus. A webhook payload "valid:<orderCode>:<amount>:<paid>" verifies,
// "{}" is the registration test, anything else has a bad signature.
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly Dictionary<long, PaymentLinkState> _links = [];

    public bool Unavailable { get; set; }

    public List<CreatePaymentLinkRequest> Created { get; } = [];

    public List<long> Cancelled { get; } = [];

    public Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new PaymentGatewayUnavailableException();
        }

        Created.Add(request);
        _links[request.OrderCode] = new PaymentLinkState(request.OrderCode, PaymentLinkStatus.Pending, request.Amount, 0);

        return Task.FromResult(new PaymentLinkResult(
            $"link-{request.OrderCode}", $"https://pay.example/{request.OrderCode}", $"qr-{request.OrderCode}",
            DateTimeOffset.UtcNow.AddMinutes(30)));
    }

    public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken)
    {
        if (rawPayload.Trim() == "{}")
        {
            return Task.FromResult(new PaymentWebhookVerificationResult(PaymentWebhookKind.NoData, null));
        }

        var parts = rawPayload.Split(':');
        return Task.FromResult(parts is ["valid", var code, var amount, var paid]
            ? new PaymentWebhookVerificationResult(PaymentWebhookKind.Valid,
                new PaymentWebhookData(long.Parse(code), long.Parse(amount), paid == "paid", $"FT{code}",
                    $$"""{"orderCode":{{code}},"amount":{{amount}}}"""))
            : new PaymentWebhookVerificationResult(PaymentWebhookKind.Invalid, null));
    }

    public Task<PaymentLinkState> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken) =>
        Unavailable ? throw new PaymentGatewayUnavailableException() : Task.FromResult(_links[orderCode]);

    public Task<PaymentLinkState> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken)
    {
        Cancelled.Add(orderCode);
        _links[orderCode] = _links[orderCode] with { Status = PaymentLinkStatus.Cancelled };

        return Task.FromResult(_links[orderCode]);
    }

    public void SetStatus(long orderCode, PaymentLinkStatus status, long? amountPaid = null) =>
        _links[orderCode] = _links[orderCode] with { Status = status, AmountPaid = amountPaid ?? _links[orderCode].AmountPaid };

    public static string Webhook(long orderCode, decimal amount, bool paid = true) => $"valid:{orderCode}:{(long)amount}:{(paid ? "paid" : "failed")}";
}
