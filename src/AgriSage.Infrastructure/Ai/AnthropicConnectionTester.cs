using Anthropic.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.Extensions.Logging;

namespace AgriSage.Infrastructure.Ai;

// "Kiểm tra kết nối" for Claude: asks the Models API about the configured model with the stored key. It generates no text,
// so it costs nothing, and it works while the provider is still turned off (to check a key before switching on).
public sealed class AnthropicConnectionTester(
    IAgriSageDbContext context,
    ISecretProtector protector,
    ILogger<AnthropicConnectionTester> logger) : IProviderConnectionTester
{
    public string Provider => AiProviderSetting.Anthropic;

    public async Task<AiProviderTestResult> TestAsync(CancellationToken cancellationToken)
    {
        var settings = await ProviderCredentials.LoadAsync(context, protector, logger, Provider, cancellationToken, requireEnabled: false);
        if (settings is null) return AiProviderConnectionTester.NoKey(protector);

        try
        {
            using var client = ClaudeDiagnosisClient.CreateClient(settings);
            var model = await client.Models.Retrieve(settings.ModelId, cancellationToken: cancellationToken);
            return new AiProviderTestResult(true, $"Kết nối được. Model: {model.DisplayName} ({model.ID}).");
        }
        catch (AnthropicUnauthorizedException)
        {
            return new AiProviderTestResult(false, "API key không hợp lệ hoặc đã bị thu hồi.");
        }
        catch (AnthropicForbiddenException)
        {
            return new AiProviderTestResult(false, "API key không có quyền dùng model này.");
        }
        catch (AnthropicNotFoundException)
        {
            return new AiProviderTestResult(false, $"Không tìm thấy model '{settings.ModelId}'.");
        }
        catch (AnthropicRateLimitException)
        {
            return new AiProviderTestResult(false, "Đang bị giới hạn tốc độ. Thử lại sau ít phút.");
        }
        catch (AnthropicApiException exception)
        {
            logger.LogWarning("AI provider test: {Error}.", exception.GetType().Name);
            return new AiProviderTestResult(false, "Nhà cung cấp AI đang lỗi. Thử lại sau.");
        }
        catch (AnthropicIOException)
        {
            return new AiProviderTestResult(false, "Không kết nối được tới nhà cung cấp AI. Kiểm tra mạng của máy chủ.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AiProviderTestResult(false, "Hết thời gian chờ nhà cung cấp AI.");
        }
    }
}
