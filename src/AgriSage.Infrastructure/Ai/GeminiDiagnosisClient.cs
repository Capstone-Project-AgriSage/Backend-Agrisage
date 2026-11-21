using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.Extensions.Logging;

namespace AgriSage.Infrastructure.Ai;

// Photo diagnosis with Gemini through the Gemini API (Google AI Studio key), REST generateContent (AI_DIAGNOSIS.md 2.2).
// Same prompt, schema and reading of the answer as Claude (GeneralModelPrompt). The key travels only in the x-goog-api-key
// header, never in the URL, so request logs cannot show it. No temperature is sent: newer Gemini models ignore it and it is
// announced to become an error. Every failure surfaces as AiServiceUnavailableException: the case fails and can be retried.
public sealed class GeminiDiagnosisClient(
    HttpClient http,
    IAgriSageDbContext context,
    ISecretProtector protector,
    ILogger<GeminiDiagnosisClient> logger) : IAiDiagnosisClient
{
    public const string BaseUrl = "https://generativelanguage.googleapis.com/";
    internal const string ApiKeyHeader = "x-goog-api-key";

    public async Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
    {
        var settings = await ProviderCredentials.LoadAsync(context, protector, logger, AiProviderSetting.Google, cancellationToken)
            ?? throw new AiServiceUnavailableException();
        var candidates = request.Candidates ?? [];
        if (candidates.Count == 0)
        {
            logger.LogError("Diagnosis {Case}: no candidate class for crop {Crop}.", request.CaseReference, request.CropType);
            throw new AiServiceUnavailableException();
        }

        var photo = await GeneralModelPrompt.ReadPhotoAsync(request.Image, cancellationToken);
        var labels = candidates.Select(c => c.Code).Append(GeneralModelPrompt.NoneLabel).ToList();
        var body = RequestBody(request.ContentType, photo,
            GeneralModelPrompt.CaseText(request.CropName ?? request.CropType ?? "", candidates), labels);

        var watch = Stopwatch.StartNew();
        HttpStatusCode status;
        string text;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            try
            {
                using var message = Request(HttpMethod.Post, $"{ModelPath(settings.ModelId)}:generateContent", settings.ApiKey);
                message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(message, timeout.Token);
                status = response.StatusCode;
                text = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (HttpRequestException)
            {
                logger.LogWarning("Diagnosis {Case}: the AI provider could not be reached.", request.CaseReference);
                throw new AiServiceUnavailableException();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Diagnosis {Case}: the AI provider timed out.", request.CaseReference);
                throw new AiServiceUnavailableException();
            }
        }

        watch.Stop();
        if ((int)status is < 200 or > 299)
        {
            // Only the status and the error code are logged: the error message may repeat parts of the request.
            logger.LogError("Diagnosis {Case}: the AI provider answered {Status} {Reason}.", request.CaseReference, (int)status, ErrorReason(text));
            throw new AiServiceUnavailableException();
        }

        var reply = ReadReply(text);
        if (reply.BlockReason is not null)
        {
            logger.LogWarning("Diagnosis {Case}: the provider blocked the request ({Reason}).", request.CaseReference, reply.BlockReason);
            throw new AiServiceUnavailableException();
        }

        var answer = GeneralModelPrompt.ParseAnswer(reply.Text, labels);
        if (answer is null)
        {
            logger.LogError("Diagnosis {Case}: the model's answer did not follow the schema (finish {Finish}).", request.CaseReference, reply.FinishReason);
            throw new AiServiceUnavailableException();
        }

        return GeneralModelPrompt.ToResult(answer, candidates, request.TopK, settings.ModelId, (int)watch.ElapsedMilliseconds,
            $"google:{reply.ModelVersion ?? settings.ModelId}");
    }

    internal static string ModelPath(string modelId) => $"v1beta/models/{Uri.EscapeDataString(modelId)}";

    internal static HttpRequestMessage Request(HttpMethod method, string path, string apiKey)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add(ApiKeyHeader, apiKey);
        return message;
    }

    // systemInstruction + one user turn (the photo inline, then the case text) + JSON output fixed by responseJsonSchema.
    internal static JsonObject RequestBody(string contentType, byte[] photo, string caseText, IReadOnlyList<string> labels) => new()
    {
        ["systemInstruction"] = new JsonObject
        {
            ["parts"] = new JsonArray(new JsonObject { ["text"] = GeneralModelPrompt.SystemPrompt }),
        },
        ["contents"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(
                new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = contentType, ["data"] = Convert.ToBase64String(photo) } },
                new JsonObject { ["text"] = caseText }),
        }),
        ["generationConfig"] = new JsonObject
        {
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = GeneralModelPrompt.AnswerSchema(labels, closed: false),
        },
    };

    internal sealed record Reply(string Text, string? FinishReason, string? BlockReason, string? ModelVersion);

    // The text of the first candidate (thought parts left out), why it stopped, and why the prompt was blocked if it was.
    internal static Reply ReadReply(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var block = root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var reason)
                ? reason.GetString()
                : null;
            var version = root.TryGetProperty("modelVersion", out var model) ? model.GetString() : null;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                return new Reply("", null, block ?? "NO_CANDIDATE", version);
            }

            var first = candidates[0];
            var finish = first.TryGetProperty("finishReason", out var f) ? f.GetString() : null;
            var text = new StringBuilder();
            if (first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    var thought = part.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True;
                    if (!thought && part.TryGetProperty("text", out var value)) text.Append(value.GetString());
                }
            }

            return new Reply(text.ToString(), finish, block, version);
        }
        catch (JsonException)
        {
            return new Reply("", null, null, null);
        }
    }

    // A short code for logs: the error's status and reason (e.g. "INVALID_ARGUMENT API_KEY_INVALID"), never its message.
    internal static string ErrorReason(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("error", out var error)) return "";
            var words = new List<string>();
            if (error.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String) words.Add(status.GetString()!);
            if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String) words.Add(code.GetString()!);
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String) words.Add(reason.GetString()!);
                }
            }

            return string.Join(' ', words);
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
