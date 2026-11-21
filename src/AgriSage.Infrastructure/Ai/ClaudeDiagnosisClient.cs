using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.Extensions.Logging;

namespace AgriSage.Infrastructure.Ai;

// Photo diagnosis with Claude through the official Anthropic SDK (AI_DIAGNOSIS.md 2.2). The prompt, the answer schema and the
// reading of the answer are shared with Gemini (GeneralModelPrompt). Every failure (no key, provider off, network, rate
// limit, refusal, bad answer) surfaces as AiServiceUnavailableException: the case fails and the Farmer can retry. The key
// is never logged.
public sealed class ClaudeDiagnosisClient(
    IAgriSageDbContext context,
    ISecretProtector protector,
    ILogger<ClaudeDiagnosisClient> logger) : IAiDiagnosisClient
{
    private const int MaxOutputTokens = 16000;

    // Models with server-side refusal fallbacks ("default" routes by refusal category; AnthropicBeta ServerSideFallback).
    private static readonly HashSet<string> FallbackModels = new(StringComparer.Ordinal)
    {
        "claude-opus-5-5", "claude-sonnet-5-5", "claude-opus-5", "claude-fable-5-2", "claude-fable-5-1",
    };

    public async Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
    {
        var settings = await ProviderCredentials.LoadAsync(context, protector, logger, AiProviderSetting.Anthropic, cancellationToken)
            ?? throw new AiServiceUnavailableException();
        var candidates = request.Candidates ?? [];
        if (candidates.Count == 0)
        {
            logger.LogError("Diagnosis {Case}: no candidate class for crop {Crop}.", request.CaseReference, request.CropType);
            throw new AiServiceUnavailableException();
        }

        var photo = await GeneralModelPrompt.ReadPhotoAsync(request.Image, cancellationToken);
        var labels = candidates.Select(c => c.Code).Append(GeneralModelPrompt.NoneLabel).ToList();
        var parameters = new MessageCreateParams
        {
            Model = settings.ModelId,
            MaxTokens = MaxOutputTokens,
            System = GeneralModelPrompt.SystemPrompt,
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat { Schema = AnswerSchema(labels) },
            },
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaImageBlockParam
                        {
                            Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(photo), MediaType = request.ContentType },
                        },
                        new BetaTextBlockParam { Text = GeneralModelPrompt.CaseText(request.CropName ?? request.CropType ?? "", candidates) },
                    },
                },
            ],
        };
        if (FallbackModels.Contains(settings.ModelId))
        {
            // A policy decline is re-served by a fallback model inside the same call instead of failing the case.
            parameters = parameters with { Betas = [AnthropicBeta.ServerSideFallback2026_07_01], Fallbacks = new Default() };
        }

        var watch = Stopwatch.StartNew();
        BetaMessage response;
        try
        {
            using var client = CreateClient(settings);
            response = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicRateLimitException)
        {
            logger.LogWarning("Diagnosis {Case}: the AI provider is rate limited.", request.CaseReference);
            throw new AiServiceUnavailableException();
        }
        catch (AnthropicUnauthorizedException)
        {
            logger.LogError("Diagnosis {Case}: the AI provider refused the API key.", request.CaseReference);
            throw new AiServiceUnavailableException();
        }
        catch (AnthropicApiException exception)
        {
            logger.LogError("Diagnosis {Case}: the AI provider answered {Status}.", request.CaseReference, exception.GetType().Name);
            throw new AiServiceUnavailableException();
        }
        catch (AnthropicIOException)
        {
            logger.LogWarning("Diagnosis {Case}: the AI provider could not be reached.", request.CaseReference);
            throw new AiServiceUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Diagnosis {Case}: the AI provider timed out.", request.CaseReference);
            throw new AiServiceUnavailableException();
        }

        watch.Stop();
        if (response.StopReason == "refusal")
        {
            logger.LogWarning("Diagnosis {Case}: the model declined ({Category}).", request.CaseReference, response.StopDetails?.Category);
            throw new AiServiceUnavailableException();
        }

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var part)) text.Append(part.Text);
        }

        var answer = GeneralModelPrompt.ParseAnswer(text.ToString(), labels);
        if (answer is null)
        {
            logger.LogError("Diagnosis {Case}: the model's answer did not follow the schema.", request.CaseReference);
            throw new AiServiceUnavailableException();
        }

        return GeneralModelPrompt.ToResult(answer, candidates, request.TopK, settings.ModelId, (int)watch.ElapsedMilliseconds,
            $"anthropic:{response.Model}");
    }

    internal static AnthropicClient CreateClient(ProviderCredentials settings) => new()
    {
        ApiKey = settings.ApiKey,
        Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        MaxRetries = 1,
    };

    // The shared schema in the SDK's shape, closed (additionalProperties:false) as Claude's structured output requires.
    internal static Dictionary<string, JsonElement> AnswerSchema(IReadOnlyList<string> labels) =>
        GeneralModelPrompt.AnswerSchema(labels, closed: true)
            .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
}
