using System.Collections.Concurrent;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Payments;

// IPaymentGateway that never calls payOS and moves no money (PayOS:Mode = Simulated). It exists so the whole online flow
// (checkout, payment, order confirmation, delivery) can be tested before real payOS keys are available, or when a quick
// run is wanted. Only the gateway is replaced: PayOsPaymentService still applies a paid link through its normal status
// query path, so allocation, audit and order rules are the real ones. The API refuses to start in this mode outside the
// Development environment (PayOsMode.EnsureAllowed).
//  - A link is "paid" only through ISimulatedPaymentGateway.TryMarkPaid (the Farmer-Web test button).
//  - State lives in memory: after a restart an old link is unknown and reads as EXPIRED, so its payment turns FAILED.
//  - The link id starts with "sim-", so a simulated payment is recognisable in the database.
public sealed class SimulatedPaymentGateway(
    IOptions<PayOsOptions> options,
    TimeProvider time,
    ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway, ISimulatedPaymentGateway
{
    public const string LinkIdPrefix = "sim-";

    private sealed record Link(long Amount, PaymentLinkStatus Status);

    private readonly ConcurrentDictionary<long, Link> _links = new();

    public Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        _links[request.OrderCode] = new Link(request.Amount, PaymentLinkStatus.Pending);

        // Where the real link would send the buyer back to: the result page, flagged so the web app shows the test button.
        var back = string.IsNullOrWhiteSpace(settings.ReturnUrl) ? "/payments/payos/return" : settings.ReturnUrl;
        var checkoutUrl = $"{back}{(back.Contains('?') ? '&' : '?')}simulated=1&orderCode={request.OrderCode}";
        logger.LogWarning("SIMULATED payOS link {OrderCode} created: no money moves (PayOS:Mode=Simulated, development only).", request.OrderCode);

        return Task.FromResult(new PaymentLinkResult(
            LinkIdPrefix + request.OrderCode, checkoutUrl, "SIMULATED", time.GetUtcNow().AddMinutes(settings.LinkExpiryMinutes)));
    }

    // There is no webhook in this mode: the registration test is accepted, anything else is not a valid call.
    public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentWebhookVerificationResult(
            rawPayload.Trim() == "{}" ? PaymentWebhookKind.NoData : PaymentWebhookKind.Invalid, null));

    public Task<PaymentLinkState> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken) =>
        Task.FromResult(State(orderCode));

    public Task<PaymentLinkState> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken)
    {
        if (_links.TryGetValue(orderCode, out var link) && link.Status == PaymentLinkStatus.Pending)
        {
            _links.TryUpdate(orderCode, link with { Status = PaymentLinkStatus.Cancelled }, link);
        }

        return Task.FromResult(State(orderCode));
    }

    public bool TryMarkPaid(long orderCode)
    {
        if (!_links.TryGetValue(orderCode, out var link) || link.Status != PaymentLinkStatus.Pending)
        {
            return false;
        }

        return _links.TryUpdate(orderCode, link with { Status = PaymentLinkStatus.Paid }, link);
    }

    private PaymentLinkState State(long orderCode) =>
        _links.TryGetValue(orderCode, out var link)
            ? new PaymentLinkState(orderCode, link.Status, link.Amount, link.Status == PaymentLinkStatus.Paid ? link.Amount : 0)
            : new PaymentLinkState(orderCode, PaymentLinkStatus.Expired, 0, 0);
}
