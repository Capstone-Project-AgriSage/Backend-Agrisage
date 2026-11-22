namespace AgriSage.Application.Common.Interfaces;

// CaseReference only labels the call in the AI service's logs (the case number); the service never stores the photo.
// Framework is the ACTIVE model's framework: it picks the client (ANTHROPIC = Claude, GOOGLE = Gemini, both general vision
// models; otherwise the FastAPI service). CropType/CropName and Candidates narrow a general model to the diseases of the crop the Farmer chose
// (AI_DIAGNOSIS.md 2.2); the FastAPI model ignores them (it only knows its own classes).
public sealed record AiPredictionRequest(
    Stream Image,
    string FileName,
    string ContentType,
    int TopK,
    string? CaseReference = null,
    string? Framework = null,
    string? CropType = null,
    string? CropName = null,
    IReadOnlyList<AiCandidate>? Candidates = null,
    // What the Farmer wrote with the photo. A general vision model gets it as context only (AI_DIAGNOSIS.md 2.2).
    string? FarmerNote = null);

// A class the model may answer: an active disease of the chosen crop (code, Vietnamese name and symptoms from the knowledge
// base), or the healthy class.
public sealed record AiCandidate(string Code, string Name, string? Symptoms, bool IsHealthy);

// Confidence is the probability of the predicted class: calibrated for the FastAPI model, the model's own estimate for a
// general vision model (its policy thresholds are chosen from an evaluation, AI_DIAGNOSIS.md 2.2). The policy (minimum
// confidence / margin) is applied by the backend, never by the AI service. RawConfidence, ModelName and IsStub are evidence
// kept in raw_output.
public sealed record AiPredictionResult(
    string PredictedClassLabel,
    decimal Confidence,
    IReadOnlyList<AiClassScore> TopPredictions,
    string ModelVersion,
    int? InferenceDurationMs,
    decimal? RawConfidence = null,
    string? ModelName = null,
    bool IsStub = false);

public sealed record AiClassScore(string ClassLabel, decimal Confidence);

// The framework of an AI model answered by a general vision model through its provider's API (AI_DIAGNOSIS.md 2.2): the
// framework is the provider code of ai_provider_settings (ANTHROPIC = Claude, GOOGLE = Gemini).
public static class AiFrameworks
{
    public const string Anthropic = "ANTHROPIC";
    public const string Google = "GOOGLE";

    // The provider whose key and model answer this framework, or null for a model with fixed classes (FastAPI).
    public static string? ProviderOf(string? framework)
    {
        var code = framework?.Trim().ToUpperInvariant();
        return code is Anthropic or Google ? code : null;
    }

    public static bool IsGeneralModel(string? framework) => ProviderOf(framework) is not null;
}

// Encrypts secrets kept in the database (the AI provider's API key) with a master key from configuration.
public interface ISecretProtector
{
    // False when no master key is configured: nothing can be encrypted or read back.
    bool IsConfigured { get; }

    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}

// Checks that a provider answers with its stored key and model, without diagnosing anything.
public interface IAiProviderConnectionTester
{
    Task<AiProviderTestResult> TestAsync(string provider, CancellationToken cancellationToken);
}

public sealed record AiProviderTestResult(bool Ok, string Message);
