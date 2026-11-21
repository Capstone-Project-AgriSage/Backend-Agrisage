using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Security;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// AI_DIAGNOSIS.md 2.2: only the administrator configures the general vision models (one setting per provider: Claude and
// Gemini); each API key is stored encrypted and is never sent back (only its last four characters), not even in the audit.
public sealed class AiProviderEndToEndTests(StaffHttpTests.StaffApiFactory factory) : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private const string Key = "sk-ant-api03-TESTSECRETVALUE-WXYZ";
    private const string GoogleKey = "AIzaTESTGOOGLESECRET-GK42";

    [CreditDbFact]
    public async Task Only_the_admin_sets_the_provider_and_the_key_never_comes_back()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var protector = new AesGcmSecretProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AesGcmSecretProtector.ConfigurationKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build());
        var tester = new FakeTester();
        await using var host = Start(factory, e, services =>
        {
            services.RemoveAll<ISecretProtector>();
            services.AddSingleton<ISecretProtector>(protector);
            services.RemoveAll<IAiProviderConnectionTester>();
            services.AddSingleton<IAiProviderConnectionTester>(tester);
        });
        var adminId = await host.AddMemberAsync(RoleCode.Admin, "ADMIN", "Quản trị viên", token);
        using var admin = await host.ClientAsync(adminId, "ADMIN", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);

        // The store owner cannot see or change it.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/ai-providers/ANTHROPIC", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-opus-5-5", isEnabled = false, timeoutSeconds = 60, version = 0 }, token)).StatusCode);

        // Before any save: the default model, off, no key; the server can encrypt.
        var start = await OkAsync(await admin.GetAsync("/api/admin/ai-providers/ANTHROPIC", token), token);
        Assert.Equal("claude-opus-5-5", start.GetProperty("modelId").GetString());
        Assert.False(start.GetProperty("hasApiKey").GetBoolean());
        Assert.False(start.GetProperty("isEnabled").GetBoolean());
        Assert.True(start.GetProperty("encryptionConfigured").GetBoolean());
        Assert.Equal(0, start.GetProperty("version").GetInt64());

        // It cannot be turned on without a key.
        var noKey = await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-opus-5-5", isEnabled = true, timeoutSeconds = 60, version = 0 }, token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noKey.StatusCode);

        // A new key and on: the answer shows the last four characters only.
        var saved = await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-opus-5-5", isEnabled = true, timeoutSeconds = 60, version = 0, apiKey = Key }, token);
        var savedJson = await saved.Content.ReadAsStringAsync(token);
        var body = await OkAsync(saved, token);
        Assert.True(body.GetProperty("hasApiKey").GetBoolean());
        Assert.True(body.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("WXYZ", body.GetProperty("apiKeyHint").GetString());
        Assert.DoesNotContain("TESTSECRETVALUE", savedJson);
        var version = body.GetProperty("version").GetInt64();
        Assert.DoesNotContain("TESTSECRETVALUE", await (await admin.GetAsync("/api/admin/ai-providers/ANTHROPIC", token)).Content.ReadAsStringAsync(token));

        // In the database: ciphertext only; the audit names the change, never the key.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var row = await db.AiProviderSettings.AsNoTracking().SingleAsync(x => x.Provider == AiProviderSetting.Anthropic, token);
            Assert.StartsWith("v1:", row.EncryptedApiKey);
            Assert.DoesNotContain("TESTSECRETVALUE", row.EncryptedApiKey);
            Assert.Equal(Key, protector.Unprotect(row.EncryptedApiKey!));
            var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "AI_PROVIDER_UPDATED", token);
            // The audit records that the key changed; anything named like a key, even its hint, is redacted by AuditValues.
            Assert.Contains("keyChanged", audit.NewValues);
            Assert.Contains("claude-opus-5-5", audit.NewValues);
            Assert.DoesNotContain("TESTSECRETVALUE", audit.NewValues + audit.OldValues);
            Assert.DoesNotContain("WXYZ", audit.NewValues);
        }

        // A stale version is refused; a key and a clear in one request are refused.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-sonnet-5-5", isEnabled = true, timeoutSeconds = 60, version = 0 }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-opus-5-5", isEnabled = false, timeoutSeconds = 60, version, apiKey = "x", clearApiKey = true }, token)).StatusCode);

        // Changing the model keeps the key; clearing the key turns the provider off.
        var sonnet = await OkAsync(await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-sonnet-5-5", isEnabled = true, timeoutSeconds = 90, version }, token), token);
        Assert.Equal("claude-sonnet-5-5", sonnet.GetProperty("modelId").GetString());
        Assert.True(sonnet.GetProperty("hasApiKey").GetBoolean());
        var cleared = await OkAsync(await admin.PutAsJsonAsync("/api/admin/ai-providers/ANTHROPIC",
            new { modelId = "claude-sonnet-5-5", isEnabled = true, timeoutSeconds = 90, version = sonnet.GetProperty("version").GetInt64(), clearApiKey = true }, token), token);
        Assert.False(cleared.GetProperty("hasApiKey").GetBoolean());
        Assert.False(cleared.GetProperty("isEnabled").GetBoolean());
        Assert.Equal(JsonValueKindNull, cleared.GetProperty("apiKeyHint").ValueKind);

        // "Kiểm tra kết nối" returns the tester's answer for that provider.
        var test = await OkAsync(await admin.PostAsync("/api/admin/ai-providers/ANTHROPIC/test", null, token), token);
        Assert.True(test.GetProperty("ok").GetBoolean());
        Assert.Equal(["ANTHROPIC"], tester.Providers);

        // Gemini has its own setting and key, next to Claude's; the provider code is case-insensitive in the route.
        var gemini = await OkAsync(await admin.GetAsync("/api/admin/ai-providers/google", token), token);
        Assert.Equal("GOOGLE", gemini.GetProperty("provider").GetString());
        Assert.Equal("gemini-3.8-flash", gemini.GetProperty("modelId").GetString());
        Assert.Equal(0, gemini.GetProperty("version").GetInt64());
        var geminiSaved = await admin.PutAsJsonAsync("/api/admin/ai-providers/GOOGLE",
            new { modelId = "gemini-3.6-flash", isEnabled = true, timeoutSeconds = 45, version = 0, apiKey = GoogleKey }, token);
        var geminiJson = await geminiSaved.Content.ReadAsStringAsync(token);
        var geminiBody = await OkAsync(geminiSaved, token);
        Assert.Equal("GK42", geminiBody.GetProperty("apiKeyHint").GetString());
        Assert.True(geminiBody.GetProperty("isEnabled").GetBoolean());
        Assert.DoesNotContain("TESTGOOGLESECRET", geminiJson);
        await admin.PostAsync("/api/admin/ai-providers/GOOGLE/test", null, token);
        Assert.Equal(["ANTHROPIC", "GOOGLE"], tester.Providers);

        // The list shows both providers, each with its own state; an unknown provider is 404; the store owner still gets 403.
        var list = await OkAsync(await admin.GetAsync("/api/admin/ai-providers", token), token);
        Assert.Equal(["ANTHROPIC", "GOOGLE"], list.EnumerateArray().Select(p => p.GetProperty("provider").GetString()));
        Assert.False(list[0].GetProperty("hasApiKey").GetBoolean());
        Assert.True(list[1].GetProperty("hasApiKey").GetBoolean());
        Assert.Contains("gemini-3.5-flash-lite", list[1].GetProperty("suggestedModels").EnumerateArray().Select(m => m.GetString()));
        Assert.DoesNotContain("TESTGOOGLESECRET", list.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/ai-providers/OPENAI", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/ai-providers/OPENAI",
            new { modelId = "x1", isEnabled = false, timeoutSeconds = 60, version = 0 }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/ai-providers", token)).StatusCode);

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var rows = await db.AiProviderSettings.AsNoTracking().OrderBy(x => x.Provider).ToListAsync(token);
            Assert.Equal(["ANTHROPIC", "GOOGLE"], rows.Select(r => r.Provider));
            Assert.Equal(GoogleKey, protector.Unprotect(rows[1].EncryptedApiKey!));
        }
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    private sealed class FakeTester : IAiProviderConnectionTester
    {
        public List<string> Providers { get; } = [];

        public Task<AiProviderTestResult> TestAsync(string provider, CancellationToken cancellationToken)
        {
            Providers.Add(provider);
            return Task.FromResult(new AiProviderTestResult(true, "Kết nối được."));
        }
    }
}
