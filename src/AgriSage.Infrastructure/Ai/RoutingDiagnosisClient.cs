using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;

namespace AgriSage.Infrastructure.Ai;

// Picks the client for the ACTIVE model's framework (AI_DIAGNOSIS.md 2.2): ANTHROPIC = Claude, GOOGLE = Gemini (general
// vision models called through their provider's API); any other model goes to the FastAPI service (or its simulator).
public sealed class RoutingDiagnosisClient(
    ClaudeDiagnosisClient claude,
    GeminiDiagnosisClient gemini,
    IFixedClassDiagnosisClient fixedClasses) : IAiDiagnosisClient
{
    public Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken) =>
        AiFrameworks.ProviderOf(request.Framework) switch
        {
            AiProviderSetting.Anthropic => claude.PredictAsync(request, cancellationToken),
            AiProviderSetting.Google => gemini.PredictAsync(request, cancellationToken),
            _ => fixedClasses.PredictAsync(request, cancellationToken),
        };
}

// The client of a model with fixed classes: the FastAPI service, or the simulator in Development.
public interface IFixedClassDiagnosisClient : IAiDiagnosisClient;

internal sealed class FixedClassDiagnosisClient(IAiDiagnosisClient inner) : IFixedClassDiagnosisClient
{
    public Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken) =>
        inner.PredictAsync(request, cancellationToken);
}

// The connection check of one provider.
public interface IProviderConnectionTester
{
    string Provider { get; }

    Task<AiProviderTestResult> TestAsync(CancellationToken cancellationToken);
}

// "Kiểm tra kết nối" for the provider the admin picked.
public sealed class AiProviderConnectionTester(IEnumerable<IProviderConnectionTester> testers) : IAiProviderConnectionTester
{
    public Task<AiProviderTestResult> TestAsync(string provider, CancellationToken cancellationToken) =>
        testers.FirstOrDefault(t => t.Provider == provider)?.TestAsync(cancellationToken)
        ?? Task.FromResult(new AiProviderTestResult(false, $"Chưa hỗ trợ nhà cung cấp '{provider}'."));

    internal static AiProviderTestResult NoKey(ISecretProtector protector) => new(false,
        protector.IsConfigured ? "Chưa có API key. Nhập key rồi lưu trước khi kiểm tra." : "Máy chủ chưa có khoá mã hoá (SecretEncryption:Key).");
}
