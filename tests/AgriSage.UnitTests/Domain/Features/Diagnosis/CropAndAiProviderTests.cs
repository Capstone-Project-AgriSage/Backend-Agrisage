using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Entities;

namespace AgriSage.UnitTests.Domain.Features.Diagnosis;

// AI_DIAGNOSIS.md 2.2: the crop chosen before a photo, and the AI provider the admin configures.
public class CropAndAiProviderTests
{
    [Fact]
    public void Catalog_lists_rice_first_then_the_four_added_crops()
    {
        Assert.Equal(["RICE", "DURIAN", "COFFEE", "CORN", "MANGO"], CropCatalog.All.OrderBy(c => c.Order).Select(c => c.Code));
        Assert.Equal("Lúa", CropCatalog.Find("RICE")!.Name);
        Assert.Equal(Disease.RiceCropType, CropCatalog.Rice);
    }

    [Theory]
    [InlineData(" durian ", "DURIAN", true)]
    [InlineData("Coffee", "COFFEE", true)]
    [InlineData("PEPPER", "PEPPER", false)]
    public void Codes_are_normalized_before_lookup(string sent, string normalized, bool known)
    {
        Assert.Equal(normalized, CropCatalog.Normalize(sent));
        Assert.Equal(known, CropCatalog.IsKnown(CropCatalog.Normalize(sent)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void A_blank_crop_is_no_crop(string? sent)
    {
        Assert.Null(CropCatalog.Normalize(sent));
        Assert.False(CropCatalog.IsKnown(sent));
        Assert.Null(CropCatalog.Find(sent));
    }

    [Fact]
    public void New_provider_starts_off_without_a_key()
    {
        var setting = new AiProviderSetting(" anthropic ", "claude-opus-5-5");

        Assert.Equal(AiProviderSetting.Anthropic, setting.Provider);
        Assert.False(setting.IsEnabled);
        Assert.False(setting.HasApiKey);
        Assert.Null(setting.ApiKeyHint);
        Assert.Equal(60, setting.TimeoutSeconds);
    }

    [Fact]
    public void Provider_cannot_be_turned_on_without_a_key()
    {
        var setting = new AiProviderSetting(AiProviderSetting.Anthropic, "claude-opus-5-5");

        Assert.Throws<DomainException>(() => setting.Configure("claude-opus-5-5", 60, isEnabled: true));
        Assert.False(setting.IsEnabled);
    }

    [Fact]
    public void Only_the_last_four_characters_of_the_key_are_kept_in_clear()
    {
        var setting = new AiProviderSetting(AiProviderSetting.Anthropic, "claude-opus-5-5");

        setting.SetApiKey("v1:ciphertext", "sk-ant-api03-abcdefWXYZ");
        setting.Configure("claude-sonnet-5-5", 45, isEnabled: true);

        Assert.Equal("WXYZ", setting.ApiKeyHint);
        Assert.Equal("v1:ciphertext", setting.EncryptedApiKey);
        Assert.True(setting.IsEnabled);
        Assert.Equal("claude-sonnet-5-5", setting.ModelId);
        Assert.Equal(45, setting.TimeoutSeconds);
    }

    [Fact]
    public void Clearing_the_key_turns_the_provider_off()
    {
        var setting = new AiProviderSetting(AiProviderSetting.Anthropic, "claude-opus-5-5");
        setting.SetApiKey("v1:ciphertext", "abcd1234");
        setting.Configure("claude-opus-5-5", 60, isEnabled: true);

        setting.ClearApiKey();

        Assert.False(setting.HasApiKey);
        Assert.Null(setting.ApiKeyHint);
        Assert.False(setting.IsEnabled);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(301)]
    public void Timeout_stays_between_10_and_300_seconds(int seconds)
    {
        var setting = new AiProviderSetting(AiProviderSetting.Anthropic, "claude-opus-5-5");

        Assert.Throws<DomainException>(() => setting.Configure("claude-opus-5-5", seconds, isEnabled: false));
    }

    [Fact]
    public void Model_id_is_required_and_short()
    {
        var setting = new AiProviderSetting(AiProviderSetting.Anthropic, "claude-opus-5-5");

        Assert.Throws<DomainException>(() => setting.Configure(" ", 60, isEnabled: false));
        Assert.Throws<DomainException>(() => setting.Configure(new string('a', 101), 60, isEnabled: false));
    }

    [Theory]
    [InlineData("google", "GOOGLE")]
    [InlineData(" ANTHROPIC ", "ANTHROPIC")]
    [InlineData("openai", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_providers_with_a_client_are_known(string? sent, string? normalized)
    {
        Assert.Equal(normalized, AiProviderSetting.Normalize(sent));
        if (normalized is null) Assert.Throws<DomainException>(() => new AiProviderSetting(sent ?? "", "x1"));
        else Assert.Equal(normalized, new AiProviderSetting(sent!, "x1").Provider);
    }

    [Theory]
    [InlineData("ANTHROPIC", "ANTHROPIC")]
    [InlineData("google", "GOOGLE")]
    [InlineData("PyTorch", null)]
    [InlineData(null, null)]
    public void A_general_model_framework_names_its_provider(string? framework, string? provider)
    {
        // The framework of an AI model decides which client answers it (AI_DIAGNOSIS.md 2.2): Claude, Gemini or FastAPI.
        Assert.Equal(provider, AiFrameworks.ProviderOf(framework));
        Assert.Equal(provider is not null, AiFrameworks.IsGeneralModel(framework));
    }
}
