using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Payments;

// The payOS adapter with the real SDK, offline: webhook signatures are made here with a test checksum key exactly as
// payOS makes them (HMAC-SHA256 over the `data` fields sorted by name, "key=value" joined with "&"). No network.
public class PayOsPaymentGatewayTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string ChecksumKey = "test-only-checksum-key-not-a-secret";

    private static PayOsPaymentGateway Gateway(bool configured = true) => new(
        new HttpClient(),
        Options.Create(configured
            ? new PayOsOptions
            {
                ClientId = "test-client", ApiKey = "test-api-key", ChecksumKey = ChecksumKey,
                ReturnUrl = "https://shop.example/paid", CancelUrl = "https://shop.example/cancelled"
            }
            : new PayOsOptions()),
        TimeProvider.System,
        NullLogger<PayOsPaymentGateway>.Instance);

    private static Dictionary<string, object> Data(long orderCode, long amount, string code = "00") => new()
    {
        ["orderCode"] = orderCode, ["amount"] = amount, ["description"] = "PM202610050001", ["accountNumber"] = "12345678",
        ["reference"] = "TF230204212323", ["transactionDateTime"] = "2026-10-05 18:25:00", ["currency"] = "VND",
        ["paymentLinkId"] = "124c33293c43417ab7879e14c8d9eb18", ["code"] = code, ["desc"] = "Thành công",
        ["counterAccountBankId"] = "", ["counterAccountBankName"] = "", ["counterAccountName"] = "",
        ["counterAccountNumber"] = "", ["virtualAccountName"] = "", ["virtualAccountNumber"] = ""
    };

    private static string Sign(Dictionary<string, object> data)
    {
        var text = string.Join("&", data.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}={d.Value}"));
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ChecksumKey), Encoding.UTF8.GetBytes(text)));
    }

    private static string Payload(Dictionary<string, object> data, string? signature = null, bool success = true) =>
        JsonSerializer.Serialize(new { code = "00", desc = "success", success, data, signature = signature ?? Sign(data) });

    [Fact]
    public async Task A_correctly_signed_paid_webhook_is_valid_and_paid()
    {
        var result = await Gateway().VerifyWebhookAsync(Payload(Data(1759650000123, 110_000)), Token);

        Assert.Equal(PaymentWebhookKind.Valid, result.Kind);
        Assert.Equal(1759650000123, result.Data!.OrderCode);
        Assert.Equal(110_000, result.Data.Amount);
        Assert.True(result.Data.IsPaid);
        Assert.Equal("TF230204212323", result.Data.Reference);
        Assert.Contains("\"orderCode\":1759650000123", result.Data.RawData);
    }

    [Fact]
    public async Task A_failed_transfer_is_valid_but_not_paid()
    {
        Assert.False((await Gateway().VerifyWebhookAsync(Payload(Data(42, 5_000, code: "01"), success: false), Token)).Data!.IsPaid);
    }

    [Fact]
    public async Task A_wrong_signature_or_a_changed_amount_is_invalid()
    {
        var data = Data(42, 5_000);
        var signature = Sign(data);
        data["amount"] = 500_000L;

        Assert.Equal(PaymentWebhookKind.Invalid, (await Gateway().VerifyWebhookAsync(Payload(data, signature), Token)).Kind);
        Assert.Equal(PaymentWebhookKind.Invalid, (await Gateway().VerifyWebhookAsync(Payload(Data(42, 5_000), new string('0', 64)), Token)).Kind);
        Assert.Equal(PaymentWebhookKind.Invalid, (await Gateway().VerifyWebhookAsync("not json", Token)).Kind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"code\":\"00\",\"desc\":\"test\"}")]
    [InlineData("{\"data\":{\"orderCode\":123}}")]
    [InlineData("[]")]
    public async Task A_body_without_data_and_signature_is_the_registration_test(string payload) =>
        Assert.Equal(PaymentWebhookKind.NoData, (await Gateway(configured: false).VerifyWebhookAsync(payload, Token)).Kind);

    [Fact]
    public async Task Without_configuration_payos_is_unavailable()
    {
        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => Gateway(configured: false).VerifyWebhookAsync(Payload(Data(42, 5_000)), Token));
        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            Gateway(configured: false).CreatePaymentLinkAsync(new CreatePaymentLinkRequest(42, 5_000, "PM1"), Token));
        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => Gateway(configured: false).GetPaymentLinkAsync(42, Token));
    }
}
