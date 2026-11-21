using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Diagnosis;

// The key is never returned: HasApiKey and its last characters (ApiKeyHint) tell the admin which key is in place.
public sealed record AiProviderSettingsResponse(
    string Provider,
    string ModelId,
    bool IsEnabled,
    int TimeoutSeconds,
    bool HasApiKey,
    string? ApiKeyHint,
    bool EncryptionConfigured,
    IReadOnlyList<string> SuggestedModels,
    DateTimeOffset? UpdatedAt,
    long Version);

// ApiKey: a new key to store (null or blank keeps the current one). ClearApiKey removes it and turns the provider off.
// Version is the one last read (0 before the first save).
public sealed record AiProviderSettingsRequest(
    string ModelId,
    bool IsEnabled,
    int TimeoutSeconds,
    long Version,
    string? ApiKey = null,
    bool ClearApiKey = false);

// provider: ANTHROPIC (Claude) or GOOGLE (Gemini); any other value is 404.
public interface IAiProviderSettingsService
{
    // Every provider the backend can call, saved or not (defaults with version 0 before the first save).
    Task<IReadOnlyList<AiProviderSettingsResponse>> ListAsync(CancellationToken cancellationToken);

    Task<AiProviderSettingsResponse> GetAsync(string provider, CancellationToken cancellationToken);

    Task<AiProviderSettingsResponse> UpdateAsync(string provider, AiProviderSettingsRequest request, CancellationToken cancellationToken);

    Task<AiProviderTestResult> TestAsync(string provider, CancellationToken cancellationToken);
}

public sealed class AiProviderSettingsRequestValidator : AbstractValidator<AiProviderSettingsRequest>
{
    public AiProviderSettingsRequestValidator()
    {
        RuleFor(r => r.ModelId).NotEmpty().MaximumLength(AiProviderSetting.MaxModelIdLength)
            .Matches("^[a-z0-9][a-z0-9.\\-]+$").WithMessage("ModelId is a model name such as claude-opus-5-5 or gemini-3.8-flash.");
        RuleFor(r => r.TimeoutSeconds).InclusiveBetween(AiProviderSetting.MinTimeoutSeconds, AiProviderSetting.MaxTimeoutSeconds);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.ApiKey).MaximumLength(500);
        RuleFor(r => r).Must(r => !(r.ClearApiKey && !string.IsNullOrWhiteSpace(r.ApiKey)))
            .WithName("apiKey").WithMessage("Send a new key or clear the key, not both.");
    }
}

// The general vision models for photo diagnosis (AI_DIAGNOSIS.md 2.2), set by the admin. One row per provider; the API key
// is encrypted before it is stored and never leaves the server again.
public sealed class AiProviderSettingsService(
    IAgriSageDbContext context,
    ISecretProtector protector,
    IAiProviderConnectionTester tester,
    IRowLockService locks,
    AuditTrail audit) : IAiProviderSettingsService
{
    // The model a provider starts with and the ones the admin page suggests; the admin may enter another model id.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SuggestedModels { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [AiProviderSetting.Anthropic] = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"],
        [AiProviderSetting.Google] = ["gemini-3.8-flash", "gemini-3.6-flash", "gemini-3.5-flash-lite"],
    };

    public static string DefaultModel(string provider) => SuggestedModels[provider][0];

    public async Task<IReadOnlyList<AiProviderSettingsResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var saved = await context.AiProviderSettings.AsNoTracking().ToListAsync(cancellationToken);
        return AiProviderSetting.Providers.Select(p => ToResponse(p, saved.FirstOrDefault(s => s.Provider == p))).ToList();
    }

    public async Task<AiProviderSettingsResponse> GetAsync(string provider, CancellationToken cancellationToken)
    {
        var code = Known(provider);
        var setting = await context.AiProviderSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Provider == code, cancellationToken);

        return ToResponse(code, setting);
    }

    public async Task<AiProviderSettingsResponse> UpdateAsync(
        string provider, AiProviderSettingsRequest request, CancellationToken cancellationToken)
    {
        var code = Known(provider);
        var store = await ActiveStore.GetIdAsync(context, cancellationToken);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockStoreAsync(store, cancellationToken);
        var setting = await context.AiProviderSettings
            .FirstOrDefaultAsync(s => s.Provider == code, cancellationToken);
        if (setting is null)
        {
            // The defaults were read with version 0. The row is first stored with them (version 0) and then changed
            // (version 1) in this one transaction, so an admin still holding the defaults cannot overwrite it.
            if (request.Version != 0) throw new ConflictException("The AI provider settings changed. Reload them before saving.");
            setting = new AiProviderSetting(code, DefaultModel(code));
            context.AiProviderSettings.Add(setting);
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (setting.Version != request.Version)
        {
            throw new ConflictException("The AI provider settings changed. Reload them before saving.");
        }

        var before = new { setting.ModelId, setting.IsEnabled, setting.TimeoutSeconds, setting.ApiKeyHint };
        var newKey = request.ApiKey?.Trim();
        var keyChanged = false;
        if (request.ClearApiKey)
        {
            keyChanged = setting.HasApiKey;
            setting.ClearApiKey();
        }
        else if (!string.IsNullOrEmpty(newKey))
        {
            if (!protector.IsConfigured)
            {
                throw new BusinessRuleException(
                    "The server has no encryption key (SecretEncryption:Key), so an API key cannot be stored. Ask the operator to set it.");
            }

            setting.SetApiKey(protector.Protect(newKey), newKey);
            keyChanged = true;
        }

        // Clearing the key always turns the provider off: it cannot work without one.
        setting.Configure(request.ModelId, request.TimeoutSeconds, !request.ClearApiKey && request.IsEnabled);

        // Never the key itself: only whether it changed and its last characters.
        audit.Record(
            "AI_PROVIDER_UPDATED", "AI_PROVIDER_SETTING", setting.Id, store,
            oldValues: before,
            newValues: new { setting.Provider, setting.ModelId, setting.IsEnabled, setting.TimeoutSeconds, setting.ApiKeyHint, KeyChanged = keyChanged });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(code, setting);
    }

    public Task<AiProviderTestResult> TestAsync(string provider, CancellationToken cancellationToken) =>
        tester.TestAsync(Known(provider), cancellationToken);

    private static string Known(string provider) =>
        AiProviderSetting.Normalize(provider) ?? throw new NotFoundException("AI provider", provider);

    private AiProviderSettingsResponse ToResponse(string provider, AiProviderSetting? setting) => setting is null
        ? new AiProviderSettingsResponse(provider, DefaultModel(provider), false, 60, false, null,
            protector.IsConfigured, SuggestedModels[provider], null, 0)
        : new AiProviderSettingsResponse(setting.Provider, setting.ModelId, setting.IsEnabled, setting.TimeoutSeconds,
            setting.HasApiKey, setting.ApiKeyHint, protector.IsConfigured, SuggestedModels[provider], setting.UpdatedAt, setting.Version);
}
