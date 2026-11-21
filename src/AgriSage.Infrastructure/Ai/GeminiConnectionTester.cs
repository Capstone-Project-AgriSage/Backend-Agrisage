using System.Net;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.Extensions.Logging;

namespace AgriSage.Infrastructure.Ai;

// "Kiểm tra kết nối" for Gemini: models.get for the configured model with the stored key (GET v1beta/models/{model}). It
// generates nothing, so it costs nothing, and works while the provider is still off.
public sealed class GeminiConnectionTester(
    HttpClient http,
    IAgriSageDbContext context,
    ISecretProtector protector,
    ILogger<GeminiConnectionTester> logger) : IProviderConnectionTester
{
    private const int MaxTestSeconds = 30;

    public string Provider => AiProviderSetting.Google;

    public async Task<AiProviderTestResult> TestAsync(CancellationToken cancellationToken)
    {
        var settings = await ProviderCredentials.LoadAsync(context, protector, logger, Provider, cancellationToken, requireEnabled: false);
        if (settings is null) return AiProviderConnectionTester.NoKey(protector);

        HttpStatusCode status;
        string body;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(settings.TimeoutSeconds, MaxTestSeconds)));
            try
            {
                using var message = GeminiDiagnosisClient.Request(HttpMethod.Get, GeminiDiagnosisClient.ModelPath(settings.ModelId), settings.ApiKey);
                using var response = await http.SendAsync(message, timeout.Token);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (HttpRequestException)
            {
                return new AiProviderTestResult(false, "Không kết nối được tới nhà cung cấp AI. Kiểm tra mạng của máy chủ.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AiProviderTestResult(false, "Hết thời gian chờ nhà cung cấp AI.");
            }
        }

        return Describe(status, body, settings.ModelId, logger);
    }

    internal static AiProviderTestResult Describe(HttpStatusCode status, string body, string modelId, ILogger logger)
    {
        var reason = GeminiDiagnosisClient.ErrorReason(body);
        switch ((int)status)
        {
            case >= 200 and <= 299:
                return Found(body, modelId);
            // An invalid key has been answered 400 API_KEY_INVALID by generateContent and 401 by the newer endpoints.
            case 401:
            case 400 when reason.Contains("API_KEY_INVALID", StringComparison.Ordinal):
                return new AiProviderTestResult(false, "API key không hợp lệ hoặc đã bị thu hồi.");
            case 403:
                return new AiProviderTestResult(false, "API key không có quyền dùng Gemini API, hoặc key đã bị khoá. Kiểm tra trong Google AI Studio.");
            case 404:
                return new AiProviderTestResult(false, $"Không tìm thấy model '{modelId}'.");
            case 429:
                return new AiProviderTestResult(false, "Đang bị giới hạn tốc độ hoặc hết hạn mức. Thử lại sau.");
            case 400:
                return new AiProviderTestResult(false, $"Nhà cung cấp từ chối yêu cầu. Kiểm tra mã model '{modelId}'.");
            default:
                logger.LogWarning("AI provider test: {Status} {Reason}.", (int)status, reason);
                return new AiProviderTestResult(false, "Nhà cung cấp AI đang lỗi. Thử lại sau.");
        }
    }

    // The model answers generateContent: it can diagnose. displayName is shown to the admin.
    private static AiProviderTestResult Found(string body, string modelId)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var name = root.TryGetProperty("displayName", out var display) ? display.GetString() : null;
            if (root.TryGetProperty("supportedGenerationMethods", out var methods) && methods.ValueKind == JsonValueKind.Array
                && !methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
            {
                return new AiProviderTestResult(false, $"Model '{modelId}' không dùng được để chẩn đoán ảnh (không hỗ trợ generateContent).");
            }

            return new AiProviderTestResult(true, $"Kết nối được. Model: {name ?? modelId} ({modelId}).");
        }
        catch (JsonException)
        {
            return new AiProviderTestResult(true, $"Kết nối được. Model: {modelId}.");
        }
    }
}
