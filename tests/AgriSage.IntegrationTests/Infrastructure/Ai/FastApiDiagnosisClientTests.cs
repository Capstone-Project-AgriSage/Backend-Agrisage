using System.Net;
using System.Text;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Ai;

// The HTTP adapter of the Python AI service, against a fake HTTP handler (no network).
public sealed class FastApiDiagnosisClientTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private const string GoodAnswer = """
        {
          "predicted_class_label": "LEAF_BLAST",
          "confidence": 0.842113,
          "raw_confidence": 0.91302,
          "top_predictions": [
            {"class_label": "LEAF_BLAST", "confidence": 0.842113},
            {"class_label": "BROWN_SPOT", "confidence": 0.101554},
            {"class_label": "HEALTHY", "confidence": 0.03109}
          ],
          "model_name": "agrisage-rice-classifier",
          "model_version": "0.1.0",
          "inference_duration_ms": 83,
          "stub": false
        }
        """;

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

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static FastApiDiagnosisClient Client(Handler handler, AiServiceOptions? options = null) => new(
        new HttpClient(handler),
        Options.Create(options ?? new AiServiceOptions { BaseUrl = "http://ai.local:8001", ApiKey = "key-123", TimeoutSeconds = 5 }),
        NullLogger<FastApiDiagnosisClient>.Instance);

    private static AiPredictionRequest Request(string? caseReference = "DG-20261110-0001") =>
        new(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]), "photo.jpg", "image/jpeg", 5, caseReference);

    [Fact]
    public async Task A_normal_answer_becomes_a_prediction_with_the_calibrated_confidence()
    {
        var client = Client(new Handler((_, _) => Task.FromResult(Json(GoodAnswer))));

        var result = await client.PredictAsync(Request(), Token);

        Assert.Equal("LEAF_BLAST", result.PredictedClassLabel);
        Assert.Equal(0.842113m, result.Confidence);
        Assert.Equal(0.91302m, result.RawConfidence);
        Assert.Equal("0.1.0", result.ModelVersion);
        Assert.Equal("agrisage-rice-classifier", result.ModelName);
        Assert.Equal(83, result.InferenceDurationMs);
        Assert.False(result.IsStub);
        Assert.Equal(["LEAF_BLAST", "BROWN_SPOT", "HEALTHY"], result.TopPredictions.Select(t => t.ClassLabel));
        Assert.Equal(0.101554m, result.TopPredictions[1].Confidence);
    }

    [Fact]
    public async Task The_request_is_a_multipart_post_with_the_key_the_photo_and_the_top_k()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(GoodAnswer)));

        await Client(handler).PredictAsync(Request(), Token);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://ai.local:8001/v1/inference", request.RequestUri!.ToString());
        Assert.Equal("key-123", Assert.Single(request.Headers.GetValues("X-Api-Key")));
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("name=image", body);
        Assert.Contains("name=top_k", body);
        Assert.Contains("name=case_id", body);
        Assert.Contains("DG-20261110-0001", body);
    }

    [Fact]
    public async Task A_base_url_with_a_trailing_slash_and_no_key_works()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(GoodAnswer)));
        var client = Client(handler, new AiServiceOptions { BaseUrl = "http://ai.local:8001/", TimeoutSeconds = 5 });

        await client.PredictAsync(Request(null), Token);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://ai.local:8001/v1/inference", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("X-Api-Key"));
        Assert.DoesNotContain("name=case_id", Assert.Single(handler.Bodies));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_success_status_is_reported_as_unavailable(HttpStatusCode status)
    {
        var client = Client(new Handler((_, _) => Task.FromResult(Json("""{"detail":"x"}""", status))));

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
    }

    [Fact]
    public async Task A_network_error_is_reported_as_unavailable()
    {
        var client = Client(new Handler((_, _) => throw new HttpRequestException("connection refused")));

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
    }

    [Fact]
    public async Task A_timeout_is_reported_as_unavailable()
    {
        var client = Client(
            new Handler(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Json(GoodAnswer);
            }),
            new AiServiceOptions { BaseUrl = "http://ai.local:8001", TimeoutSeconds = 1 });

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
    }

    [Fact]
    public async Task A_cancellation_by_the_caller_is_not_hidden_as_unavailable()
    {
        var client = Client(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json(GoodAnswer);
        }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PredictAsync(Request(), cancelled.Token));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"predicted_class_label":"LEAF_BLAST","confidence":1.5,"top_predictions":[],"model_version":"1"}""")]
    [InlineData("""{"predicted_class_label":"","confidence":0.9,"top_predictions":[],"model_version":"1"}""")]
    [InlineData("""{"predicted_class_label":"LEAF_BLAST","confidence":0.9,"top_predictions":[],"model_version":" "}""")]
    [InlineData("""{"predicted_class_label":"LEAF_BLAST","confidence":0.9,"model_version":"1"}""")]
    [InlineData("""{"predicted_class_label":"LEAF_BLAST","confidence":0.9,"top_predictions":[{"class_label":"X","confidence":-0.2}],"model_version":"1"}""")]
    public async Task An_answer_that_is_not_a_usable_prediction_is_reported_as_unavailable(string body)
    {
        var client = Client(new Handler((_, _) => Task.FromResult(Json(body))));

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ai.local")]
    public async Task Without_a_usable_base_url_nothing_is_called(string? baseUrl)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(GoodAnswer)));
        var client = Client(handler, new AiServiceOptions { BaseUrl = baseUrl });

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => client.PredictAsync(Request(), Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_stub_answer_is_flagged()
    {
        var stubbed = GoodAnswer.Replace("\"stub\": false", "\"stub\": true");
        var client = Client(new Handler((_, _) => Task.FromResult(Json(stubbed))));

        var result = await client.PredictAsync(Request(), Token);

        Assert.True(result.IsStub);
    }
}

public sealed class SimulatedDiagnosisClientTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SimulatedDiagnosisClient Client(string version = "simulated") =>
        new(Options.Create(new AiServiceOptions { SimulatedModelVersion = version }));

    private static AiPredictionRequest Photo(byte seed, int topK = 5) =>
        new(new MemoryStream(Enumerable.Range(0, 64).Select(i => (byte)(i * seed)).ToArray()), "p.jpg", "image/jpeg", topK);

    [Fact]
    public async Task The_same_photo_always_gets_the_same_prediction()
    {
        var first = await Client().PredictAsync(Photo(3), Token);
        var second = await Client().PredictAsync(Photo(3), Token);

        Assert.Equal(first.PredictedClassLabel, second.PredictedClassLabel);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.TopPredictions, second.TopPredictions);
    }

    [Fact]
    public async Task The_answer_is_a_flagged_distribution_over_the_five_classes()
    {
        var result = await Client("sim-1").PredictAsync(Photo(7), Token);

        Assert.True(result.IsStub);
        Assert.Equal("sim-1", result.ModelVersion);
        Assert.Equal(5, result.TopPredictions.Count);
        Assert.Equal(
            ["BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "HEALTHY", "LEAF_BLAST", "SHEATH_BLIGHT"],
            result.TopPredictions.Select(t => t.ClassLabel).Order());
        Assert.Equal(result.TopPredictions.Select(t => t.Confidence).OrderByDescending(c => c), result.TopPredictions.Select(t => t.Confidence));
        Assert.Equal(result.TopPredictions[0].ClassLabel, result.PredictedClassLabel);
        Assert.Equal(result.TopPredictions[0].Confidence, result.Confidence);
        Assert.InRange(result.TopPredictions.Sum(t => t.Confidence), 0.999m, 1.001m);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(9, 5)]
    public async Task Top_k_is_honoured_within_the_five_classes(int topK, int expected)
    {
        var result = await Client().PredictAsync(Photo(5, topK), Token);

        Assert.Equal(expected, result.TopPredictions.Count);
    }
}

public sealed class AiServiceModeTests
{
    private static IConfiguration Config(string? mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(mode is null ? [] : new Dictionary<string, string?> { ["AiService:Mode"] = mode })
            .Build();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Real", false)]
    [InlineData("real", false)]
    [InlineData("Simulated", true)]
    [InlineData(" simulated ", true)]
    public void The_mode_defaults_to_real(string? mode, bool simulated) =>
        Assert.Equal(simulated, AiServiceMode.IsSimulated(Config(mode)));

    [Fact]
    public void An_unknown_mode_is_a_startup_error() =>
        Assert.Throws<InvalidOperationException>(() => AiServiceMode.IsSimulated(Config("Fake")));

    [Fact]
    public void Simulated_predictions_are_refused_outside_development()
    {
        AiServiceMode.EnsureAllowed(simulated: true, isDevelopment: true);
        AiServiceMode.EnsureAllowed(simulated: false, isDevelopment: false);
        Assert.Throws<InvalidOperationException>(() => AiServiceMode.EnsureAllowed(simulated: true, isDevelopment: false));
    }
}
