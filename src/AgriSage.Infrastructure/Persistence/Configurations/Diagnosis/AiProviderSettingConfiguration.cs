using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Diagnosis;

// ai_provider_settings (AI_DIAGNOSIS.md 2.2): the general vision model the admin configured, one row per provider. The API
// key column holds ciphertext only (AesGcmSecretProtector); the master key lives in configuration, never in the database.
internal sealed class AiProviderSettingConfiguration : EntityConfiguration<AiProviderSetting>
{
    protected override string TableName => "ai_provider_settings";

    protected override void ConfigureEntity(EntityTypeBuilder<AiProviderSetting> builder)
    {
        builder.Property(setting => setting.Provider).HasMaxLength(30);
        builder.Property(setting => setting.ModelId).HasMaxLength(AiProviderSetting.MaxModelIdLength);
        builder.Property(setting => setting.EncryptedApiKey).IsText();
        builder.Property(setting => setting.ApiKeyHint).HasMaxLength(AiProviderSetting.HintLength);
        builder.Property(setting => setting.IsEnabled).HasDbDefault(false);
        builder.Property(setting => setting.TimeoutSeconds).HasDbDefault(60);
        builder.Property(setting => setting.Version).HasDbDefault(0L);
        builder.Ignore(setting => setting.HasApiKey);

        builder.HasIndex(setting => setting.Provider, "ux_ai_provider_settings_provider")
            .HasDatabaseName("ux_ai_provider_settings_provider").IsUnique().HasFilter("deleted_at IS NULL");

        builder.HasCheck("provider", "provider IN ('ANTHROPIC', 'GOOGLE')");
        builder.HasCheck("timeout_seconds", $"timeout_seconds BETWEEN {AiProviderSetting.MinTimeoutSeconds} AND {AiProviderSetting.MaxTimeoutSeconds}");
        // Turned on only with a key.
        builder.HasCheck("enabled_needs_key", "is_enabled = FALSE OR encrypted_api_key IS NOT NULL");
    }
}
