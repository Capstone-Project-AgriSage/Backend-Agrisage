using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayOS;
using PayOS.Exceptions;
using PayOS.Models;
using PayOS.Models.Webhooks;
using Sdk = PayOS.Models.V2.PaymentRequests;

namespace AgriSage.Infrastructure.Payments;

// IPaymentGateway over the official payOS SDK 2.1.0 (decision C-D7) — the only place that uses the SDK. Link creation
// runs with MaxRetries = 0 so a timeout never creates two links; an uncertain creation is answered "unavailable" and the
// link (if payOS made one) is cancelled, so no payable link is left without its payment. Provider errors are logged by
// type and status only (never keys, signatures or payloads) and surface as PaymentGatewayUnavailableException (503).
public sealed class PayOsPaymentGateway(
    HttpClient httpClient,
    IOptions<PayOsOptions> options,
    TimeProvider time,
    ILogger<PayOsPaymentGateway> logger) : IPaymentGateway
{
    public async Task<PaymentLinkResult> CreatePaymentLinkAsync(CreatePaymentLinkRequest request, CancellationToken cancellationToken)
    {
        var settings = RequireConfigured();
        var expiresAt = time.GetUtcNow().AddMinutes(settings.LinkExpiryMinutes);
        var client = Client(settings, maxRetries: 0);

        try
        {
            var link = await client.PaymentRequests.CreateAsync(
                new Sdk.CreatePaymentLinkRequest
                {
                    OrderCode = request.OrderCode,
                    Amount = request.Amount,
                    Description = request.Description,
                    ReturnUrl = settings.ReturnUrl!,
                    CancelUrl = settings.CancelUrl!,
                    ExpiredAt = expiresAt.ToUnixTimeSeconds()
                },
                new RequestOptions<Sdk.CreatePaymentLinkRequest> { MaxRetries = 0, CancellationToken = cancellationToken });

            return new PaymentLinkResult(
                link.PaymentLinkId, link.CheckoutUrl, link.QrCode,
                link.ExpiredAt is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : expiresAt);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            logger.LogWarning("payOS refused or did not answer a payment link creation ({Error}).", Describe(exception));
            await CancelQuietlyAsync(client, request.OrderCode);
            throw new PaymentGatewayUnavailableException();
        }
    }

    public async Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string rawPayload, CancellationToken cancellationToken)
    {
        JsonElement data;
        Webhook? webhook;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.String)
            {
                return new PaymentWebhookVerificationResult(PaymentWebhookKind.NoData, null);
            }

            data = data.Clone();
            webhook = JsonSerializer.Deserialize<Webhook>(rawPayload);
        }
        catch (JsonException)
        {
            return new PaymentWebhookVerificationResult(PaymentWebhookKind.Invalid, null);
        }

        if (webhook?.Data is null)
        {
            return new PaymentWebhookVerificationResult(PaymentWebhookKind.Invalid, null);
        }

        try
        {
            var verified = await Client(RequireConfigured(), maxRetries: null).Webhooks.VerifyAsync(webhook);

            return new PaymentWebhookVerificationResult(
                PaymentWebhookKind.Valid,
                new PaymentWebhookData(
                    verified.OrderCode, verified.Amount, webhook.Success && verified.Code == "00",
                    string.IsNullOrWhiteSpace(verified.Reference) ? null : verified.Reference, data.GetRawText()));
        }
        catch (Exception exception) when (exception is InvalidSignatureException or WebhookException)
        {
            return new PaymentWebhookVerificationResult(PaymentWebhookKind.Invalid, null);
        }
    }

    public async Task<PaymentLinkState> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken)
    {
        try
        {
            var link = await Client(RequireConfigured(), maxRetries: null).PaymentRequests.GetAsync(
                orderCode, new RequestOptions { CancellationToken = cancellationToken });

            return State(link);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            logger.LogWarning("payOS status query failed ({Error}).", Describe(exception));
            throw new PaymentGatewayUnavailableException();
        }
    }

    public async Task<PaymentLinkState> CancelPaymentLinkAsync(long orderCode, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var link = await Client(RequireConfigured(), maxRetries: null).PaymentRequests.CancelAsync(
                orderCode, reason, new RequestOptions<Sdk.CancelPaymentLinkRequest> { CancellationToken = cancellationToken });

            return State(link);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            logger.LogWarning("payOS refused a payment link cancellation ({Error}).", Describe(exception));
            throw new PaymentGatewayUnavailableException();
        }
    }

    private static PaymentLinkState State(Sdk.PaymentLink link) =>
        new(link.OrderCode, link.Status switch
        {
            Sdk.PaymentLinkStatus.Pending => PaymentLinkStatus.Pending,
            Sdk.PaymentLinkStatus.Processing => PaymentLinkStatus.Processing,
            Sdk.PaymentLinkStatus.Paid => PaymentLinkStatus.Paid,
            Sdk.PaymentLinkStatus.Underpaid => PaymentLinkStatus.Underpaid,
            Sdk.PaymentLinkStatus.Cancelled => PaymentLinkStatus.Cancelled,
            Sdk.PaymentLinkStatus.Expired => PaymentLinkStatus.Expired,
            _ => PaymentLinkStatus.Failed
        }, link.Amount, link.AmountPaid);

    private async Task CancelQuietlyAsync(PayOSClient client, long orderCode)
    {
        try
        {
            await client.PaymentRequests.CancelAsync(orderCode, "Link creation did not complete.",
                new RequestOptions<Sdk.CancelPaymentLinkRequest> { CancellationToken = CancellationToken.None });
        }
        catch (Exception exception) when (exception is PayOSException or HttpRequestException or TaskCanceledException)
        {
            // Nothing was created, or payOS is down: there is no link to close.
        }
    }

    private PayOSClient Client(PayOsOptions settings, int? maxRetries) =>
        new(new PayOSOptions
        {
            ClientId = settings.ClientId!,
            ApiKey = settings.ApiKey!,
            ChecksumKey = settings.ChecksumKey!,
            HttpClient = httpClient,
            MaxRetries = maxRetries ?? 2
        });

    private PayOsOptions RequireConfigured()
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            logger.LogError("payOS is not configured (PayOS:ClientId, ApiKey, ChecksumKey, ReturnUrl, CancelUrl).");
            throw new PaymentGatewayUnavailableException();
        }

        return settings;
    }

    // Provider-side problems only; the caller cancelling the request is not one of them.
    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is PayOSException or HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string Describe(Exception exception) =>
        exception is ApiException api ? $"{exception.GetType().Name}, status {api.StatusCode}" : exception.GetType().Name;
}
