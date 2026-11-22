using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Infrastructure.Ai;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Security;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgriSage.IntegrationTests.Infrastructure.Ai;

// AI_DIAGNOSIS.md 2.2, Gemini through the Gemini API (generateContent, Google AI Studio key): the same prompt and answer
// rules as Claude, the key only in the x-goog-api-key header, every failure an AiServiceUnavailableException. A fake HTTP
// handler answers; nothing reaches Google.
public sealed class GeminiDiagnosisClientTests
{
    private const string Key = "AIzaTESTGEMINISECRET-9QZ1";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyList<AiCandidate> Durian =
    [
        new("DURIAN_PHYTOPHTHORA", "Thối gốc chảy nhựa", "Vỏ thân nứt, chảy nhựa nâu đỏ", false),
        new("DURIAN_ANTHRACNOSE", "Thán thư", "Đốm nâu lõm trên lá non", false),
        new("HEALTHY", "Khỏe mạnh", null, true),
    ];

    private static readonly IReadOnlyList<string> Labels = [.. Durian.Select(c => c.Code), GeneralModelPrompt.NoneLabel];

    private static string Reply(string answer, string finish = "STOP") => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new
            {
                content = new { role = "model", parts = new object[] { new { text = "thinking...", thought = true }, new { text = answer } } },
                finishReason = finish,
            },
        },
        modelVersion = "gemini-3.6-flash-001",
    });

    private const string GoodAnswer =
        """{"label":"DURIAN_PHYTOPHTHORA","confidence":0.81,"alternatives":[{"label":"DURIAN_ANTHRACNOSE","confidence":0.12}],"reason":"Vỏ thân chảy nhựa"}""";

    [Fact]
    public void The_request_carries_the_prompt_the_inline_photo_and_the_schema_but_no_temperature()
    {
        var body = GeminiDiagnosisClient.RequestBody("image/jpeg", [1, 2, 3], "Crop chosen by the farmer: Sầu riêng", Labels);
        using var document = JsonDocument.Parse(body.ToJsonString());
        var root = document.RootElement;

        Assert.Equal(GeneralModelPrompt.SystemPrompt, root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var turn = root.GetProperty("contents")[0];
        Assert.Equal("user", turn.GetProperty("role").GetString());
        var image = turn.GetProperty("parts")[0].GetProperty("inlineData");
        Assert.Equal("image/jpeg", image.GetProperty("mimeType").GetString());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), image.GetProperty("data").GetString());
        Assert.Contains("Sầu riêng", turn.GetProperty("parts")[1].GetProperty("text").GetString());

        var config = root.GetProperty("generationConfig");
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        var schema = config.GetProperty("responseJsonSchema");
        Assert.Equal(Labels, schema.GetProperty("properties").GetProperty("label").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.False(schema.TryGetProperty("additionalProperties", out _), "only Claude needs a closed schema");
        Assert.False(config.TryGetProperty("temperature", out _), "newer Gemini models ignore it and will refuse it");
    }

    [Fact]
    public void A_reply_gives_the_answer_text_without_thought_parts()
    {
        var reply = GeminiDiagnosisClient.ReadReply(Reply(GoodAnswer));

        Assert.Equal(GoodAnswer, reply.Text);
        Assert.Equal("STOP", reply.FinishReason);
        Assert.Null(reply.BlockReason);
        Assert.Equal("gemini-3.6-flash-001", reply.ModelVersion);
    }

    [Fact]
    public void A_blocked_prompt_or_an_empty_reply_is_recognised()
    {
        Assert.Equal("SAFETY", GeminiDiagnosisClient.ReadReply("""{"promptFeedback":{"blockReason":"SAFETY"}}""").BlockReason);
        Assert.Equal("NO_CANDIDATE", GeminiDiagnosisClient.ReadReply("""{"candidates":[]}""").BlockReason);
        Assert.Equal("", GeminiDiagnosisClient.ReadReply("not json").Text);
    }

    [Fact]
    public void Error_reason_keeps_codes_never_the_message()
    {
        var classic = """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""";
        Assert.Equal("INVALID_ARGUMENT API_KEY_INVALID", GeminiDiagnosisClient.ErrorReason(classic));
        Assert.Equal("authentication", GeminiDiagnosisClient.ErrorReason("""{"error":{"code":"authentication","message":"The API key is missing, invalid, or expired."}}"""));
        Assert.Equal("", GeminiDiagnosisClient.ErrorReason("<html>"));
    }

    [Fact]
    public void The_model_path_keeps_a_plain_model_id()
    {
        Assert.Equal("v1beta/models/gemini-3.6-flash", GeminiDiagnosisClient.ModelPath("gemini-3.6-flash"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"name":"models/gemini-3.6-flash","displayName":"Gemini 3.6 Flash","supportedGenerationMethods":["generateContent","countTokens"]}""", true, "Gemini 3.6 Flash")]
    [InlineData(HttpStatusCode.OK, """{"name":"models/text-embedding","supportedGenerationMethods":["embedContent"]}""", false, "không hỗ trợ generateContent")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""", false, "API key không hợp lệ")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":"authentication"}}""", false, "API key không hợp lệ")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"status":"PERMISSION_DENIED"}}""", false, "không có quyền")]
    [InlineData(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND"}}""", false, "Không tìm thấy model")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""", false, "giới hạn tốc độ")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"status":"UNAVAILABLE"}}""", false, "đang lỗi")]
    public void The_connection_check_explains_each_answer(HttpStatusCode status, string body, bool ok, string message)
    {
        var result = GeminiConnectionTester.Describe(status, body, "gemini-3.6-flash", NullLogger.Instance);

        Assert.Equal(ok, result.Ok);
        Assert.Contains(message, result.Message);
    }

    [CreditDbFact]
    public async Task A_diagnosis_sends_the_key_in_the_header_only_and_reads_the_answer()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var protector = Protector();
        await SaveSettingAsync(e, protector, isEnabled: true);
        var handler = new Handler((_, _) => Task.FromResult(Json(Reply(GoodAnswer))));
        var logs = new ListLogger<GeminiDiagnosisClient>();

        using var s = e.Scope();
        var client = new GeminiDiagnosisClient(Http(handler), s.ServiceProvider.GetRequiredService<AgriSageDbContext>(), protector, logs);
        var result = await client.PredictAsync(Request(), Token);

        var sent = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent", sent.RequestUri!.ToString());
        Assert.Equal(Key, sent.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("TESTGEMINISECRET", sent.RequestUri.ToString());
        // The JSON escapes non-ASCII letters; read it back before looking for the Vietnamese case text.
        using (var sentBody = JsonDocument.Parse(handler.Bodies.Single()))
        {
            var caseText = sentBody.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("text").GetString();
            Assert.Contains("Thối gốc chảy nhựa", caseText);
            Assert.Contains("Crop chosen by the farmer: Sầu riêng", caseText);
            Assert.Contains("<<<Gốc cây chảy nhựa nâu sau đợt mưa>>>", caseText);
        }

        Assert.Equal("DURIAN_PHYTOPHTHORA", result.PredictedClassLabel);
        Assert.Equal(0.81m, result.Confidence);
        Assert.Equal("gemini-3.6-flash", result.ModelVersion);
        Assert.Equal("google:gemini-3.6-flash-001", result.ModelName);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("TESTGEMINISECRET", StringComparison.Ordinal));
    }

    [CreditDbFact]
    public async Task Every_failure_fails_the_case_without_showing_the_key()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var protector = Protector();
        await SaveSettingAsync(e, protector, isEnabled: true);
        var logs = new ListLogger<GeminiDiagnosisClient>();
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();

        var answers = new Func<HttpResponseMessage>[]
        {
            () => Json("""{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""", HttpStatusCode.BadRequest),
            () => Json("""{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""", HttpStatusCode.TooManyRequests),
            () => Json("""{"promptFeedback":{"blockReason":"SAFETY"}}"""),
            () => Json(Reply("""{"label":"LEAF_BLAST","confidence":0.9,"alternatives":[],"reason":"x"}""")),
            () => Json(Reply("""{"label":"DURIAN_PHYTOPHTHO""", "MAX_TOKENS")),
            () => throw new HttpRequestException("network down"),
        };
        foreach (var answer in answers)
        {
            var client = new GeminiDiagnosisClient(Http(new Handler((_, _) => Task.FromResult(answer()))), db, protector, logs);
            await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
        }

        Assert.DoesNotContain(logs.Messages, m => m.Contains("TESTGEMINISECRET", StringComparison.Ordinal) || m.Contains("API key not valid", StringComparison.Ordinal));
        Assert.Contains(logs.Messages, m => m.Contains("API_KEY_INVALID", StringComparison.Ordinal));
    }

    [CreditDbFact]
    public async Task A_provider_turned_off_is_not_called_but_its_key_can_still_be_checked()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var protector = Protector();
        await SaveSettingAsync(e, protector, isEnabled: false);
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();

        var handler = new Handler((_, _) => Task.FromResult(Json(Reply(GoodAnswer))));
        var client = new GeminiDiagnosisClient(Http(handler), db, protector, NullLogger<GeminiDiagnosisClient>.Instance);
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
        Assert.Empty(handler.Requests);

        var models = new Handler((_, _) => Task.FromResult(Json("""{"name":"models/gemini-3.6-flash","displayName":"Gemini 3.6 Flash","supportedGenerationMethods":["generateContent"]}""")));
        var tester = new GeminiConnectionTester(Http(models), db, protector, NullLogger<GeminiConnectionTester>.Instance);
        var check = await tester.TestAsync(Token);

        Assert.True(check.Ok);
        Assert.Equal(HttpMethod.Get, models.Requests.Single().Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash", models.Requests.Single().RequestUri!.ToString());
        Assert.Equal(Key, models.Requests.Single().Headers.GetValues("x-goog-api-key").Single());
    }

    private static AesGcmSecretProtector Protector() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AesGcmSecretProtector.ConfigurationKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build());

    private static async Task SaveSettingAsync(CreditTestDatabase e, AesGcmSecretProtector protector, bool isEnabled)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var setting = new AiProviderSetting(AiProviderSetting.Google, "gemini-3.6-flash", timeoutSeconds: 30);
        setting.SetApiKey(protector.Protect(Key), Key);
        setting.Configure("gemini-3.6-flash", 30, isEnabled);
        db.AiProviderSettings.Add(setting);
        await db.SaveChangesAsync(Token);
    }

    private static AiPredictionRequest Request() =>
        new(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]), "leaf.jpg", "image/jpeg", 5, "DG-20261121-0001",
            Framework: "GOOGLE", CropType: "DURIAN", CropName: "Sầu riêng", Candidates: Durian,
            FarmerNote: "Gốc cây chảy nhựa nâu sau đợt mưa");

    private static HttpClient Http(Handler handler) => new(handler) { BaseAddress = new Uri(GeminiDiagnosisClient.BaseUrl) };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request, cancellationToken);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
