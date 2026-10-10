using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Ai;

// The Python FastAPI AI service (POST {BaseUrl}/v1/inference, multipart: image, top_k, case_id). Failures of every kind
// surface as AiServiceUnavailableException without provider details; only the HTTP status is logged, never the key,
// the URL or the response body.
public sealed class FastApiDiagnosisClient(
    HttpClient httpClient,
    IOptions<AiServiceOptions> options,
    ILogger<FastApiDiagnosisClient> logger) : IAiDiagnosisClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            logger.LogError("The AI service is not configured (AiService:BaseUrl).");
            throw new AiServiceUnavailableException();
        }

        using var content = new MultipartFormDataContent();
        var image = new StreamContent(request.Image);
        image.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        content.Add(image, "image", string.IsNullOrWhiteSpace(request.FileName) ? "photo" : request.FileName);
        content.Add(new StringContent(request.TopK.ToString(CultureInfo.InvariantCulture)), "top_k");
        if (!string.IsNullOrWhiteSpace(request.CaseReference))
        {
            content.Add(new StringContent(request.CaseReference), "case_id");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl!.TrimEnd('/')}/v1/inference")
        {
            Content = content
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            message.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            response = await httpClient.SendAsync(message, timeout.Token);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("The AI service request failed (network error).");
            throw new AiServiceUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The AI service request timed out.");
            throw new AiServiceUnavailableException();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The AI service answered with status {StatusCode}.", (int)response.StatusCode);
                throw new AiServiceUnavailableException();
            }

            Answer? answer;
            try
            {
                answer = await response.Content.ReadFromJsonAsync<Answer>(Json, cancellationToken);
            }
            catch (JsonException)
            {
                answer = null;
            }

            return ToResult(answer) ?? throw Unusable();
        }
    }

    private AiServiceUnavailableException Unusable()
    {
        logger.LogWarning("The AI service answered with a body that is not a prediction.");

        return new AiServiceUnavailableException();
    }

    private static AiPredictionResult? ToResult(Answer? a)
    {
        if (a is null
            || string.IsNullOrWhiteSpace(a.PredictedClassLabel)
            || a.Confidence is null or < 0 or > 1
            || string.IsNullOrWhiteSpace(a.ModelVersion)
            || a.TopPredictions is null
            || a.TopPredictions.Any(t => string.IsNullOrWhiteSpace(t.ClassLabel) || t.Confidence is < 0 or > 1))
        {
            return null;
        }

        return new AiPredictionResult(
            a.PredictedClassLabel.Trim(),
            a.Confidence.Value,
            a.TopPredictions.Select(t => new AiClassScore(t.ClassLabel!.Trim(), t.Confidence)).ToList(),
            a.ModelVersion.Trim(),
            a.InferenceDurationMs,
            a.RawConfidence,
            a.ModelName,
            a.Stub ?? false);
    }

    private sealed record Answer(
        string? PredictedClassLabel,
        decimal? Confidence,
        decimal? RawConfidence,
        List<Score>? TopPredictions,
        string? ModelName,
        string? ModelVersion,
        int? InferenceDurationMs,
        bool? Stub);

    private sealed record Score(string? ClassLabel, decimal Confidence);
}
