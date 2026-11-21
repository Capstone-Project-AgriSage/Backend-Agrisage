using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Diagnosis.Entities;

// ai_provider_settings (AI_DIAGNOSIS.md 2.2): how the backend reaches a general vision model for photo diagnosis. One row
// per provider (Anthropic for Claude, Google for Gemini). The API key is stored ENCRYPTED (Infrastructure encrypts it with a master key that never enters the
// database); only its last characters are kept in clear so the admin can tell which key is in place.
public sealed class AiProviderSetting : SoftDeletableEntity, IHasConcurrencyVersion
{
    public const string Anthropic = "ANTHROPIC";
    public const string Google = "GOOGLE";

    // The providers the backend has a client for; the database check lists the same codes.
    public static IReadOnlyList<string> Providers { get; } = [Anthropic, Google];
    public const int MinTimeoutSeconds = 10;
    public const int MaxTimeoutSeconds = 300;
    public const int MaxModelIdLength = 100;
    public const int HintLength = 4;

    private AiProviderSetting()
    {
    }

    public AiProviderSetting(string provider, string modelId, int timeoutSeconds = 60)
    {
        Provider = Normalize(provider) ?? throw new DomainException($"Unknown AI provider '{provider}'.");
        Configure(modelId, timeoutSeconds, isEnabled: false);
    }

    public string Provider { get; private set; } = null!;

    public string ModelId { get; private set; } = null!;

    // Encrypted by Infrastructure; null = no key entered yet.
    public string? EncryptedApiKey { get; private set; }

    // The key's last characters (e.g. "…a1B2"), never the whole key.
    public string? ApiKeyHint { get; private set; }

    public bool IsEnabled { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public long Version { get; private set; }

    public bool HasApiKey => EncryptedApiKey is not null;

    // The provider code in upper case, or null when the backend has no client for it.
    public static string? Normalize(string? provider)
    {
        var code = provider?.Trim().ToUpperInvariant();
        return code is not null && Providers.Contains(code) ? code : null;
    }

    public void Configure(string modelId, int timeoutSeconds, bool isEnabled)
    {
        var model = Guard.NotNullOrWhiteSpace(modelId).Trim();
        if (model.Length > MaxModelIdLength)
        {
            throw new DomainException($"The model id must be at most {MaxModelIdLength} characters.");
        }

        if (timeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
        {
            throw new DomainException($"The timeout must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds} seconds.");
        }

        if (isEnabled && EncryptedApiKey is null)
        {
            throw new DomainException("Enter an API key before turning the provider on.");
        }

        ModelId = model;
        TimeoutSeconds = timeoutSeconds;
        IsEnabled = isEnabled;
    }

    // The caller passes the already encrypted value and the clear hint taken from the plain key.
    public void SetApiKey(string encryptedApiKey, string hint)
    {
        EncryptedApiKey = Guard.NotNullOrWhiteSpace(encryptedApiKey);
        var cleanHint = Guard.NotNullOrWhiteSpace(hint);
        ApiKeyHint = cleanHint.Length > HintLength ? cleanHint[^HintLength..] : cleanHint;
    }

    // Removing the key also turns the provider off: it cannot work without one.
    public void ClearApiKey()
    {
        EncryptedApiKey = null;
        ApiKeyHint = null;
        IsEnabled = false;
    }
}
