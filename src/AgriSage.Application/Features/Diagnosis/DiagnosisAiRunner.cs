using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Application.Features.Diagnosis;

// Runs the AI on the primary photo of a case and stores the outcome (AI_DIAGNOSIS.md D1-D6, D10).
//
// The caller has already committed the case (SUBMITTED or FAILED) with its photo. This step reads, calls the AI service
// with NO transaction and no tracked entity open, then re-reads the case and records the result in one save:
// StartProcessing -> RecordInference -> CompleteAi / FailAi. A crash in between leaves the case SUBMITTED, which a
// reviewer can run again. An AI failure never throws: it is stored as a FAILED inference (or none, without a model).
public sealed class DiagnosisAiRunner(
    IAgriSageDbContext context,
    IAiDiagnosisClient client,
    IDateTimeProvider clock,
    IOptions<AiDiagnosisOptions> options,
    ILogger<DiagnosisAiRunner> logger)
{
    // The service is always asked for the whole distribution; screens cut it to the policy's top_k (D6).
    public const int RequestedTopK = 5;

    private const string FailedLabel = "ERROR";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(Guid caseId, byte[] photo, string contentType, string fileName, CancellationToken cancellationToken)
    {
        var caseNumber = await context.DiagnosisCases.AsNoTracking()
            .Where(c => c.Id == caseId).Select(c => c.CaseNumber).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Diagnosis case", caseId);

        var now = clock.UtcNow;
        var model = await context.AiModels.AsNoTracking()
            .Where(m => m.Status == AiModelStatus.Active)
            .OrderByDescending(m => m.DeployedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var policy = model is null
            ? null
            : await context.AiPolicyConfigs.AsNoTracking()
                .Where(p => p.AiModelId == model.Id && p.Status == AiPolicyConfigStatus.Active
                    && p.EffectiveFrom <= now && (p.EffectiveTo == null || p.EffectiveTo > now))
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

        if (model is null || policy is null)
        {
            // Nothing to call and nothing to record against: the case fails and waits for a reviewer or a configured model.
            logger.LogWarning("Diagnosis case {CaseNumber}: no ACTIVE AI model with an effective ACTIVE policy.", caseNumber);
            var failed = await LoadCaseAsync(caseId, cancellationToken);
            failed.StartProcessing();
            failed.FailAi();
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        AiPredictionResult? result = null;
        string? error = null;
        try
        {
            using var stream = new MemoryStream(photo, writable: false);
            result = await client.PredictAsync(
                new AiPredictionRequest(stream, fileName, contentType, RequestedTopK, caseNumber), cancellationToken);
        }
        catch (AiServiceUnavailableException)
        {
            error = "AI_SERVICE_UNAVAILABLE";
        }

        Guid? predictedDiseaseId = null;
        if (result is not null)
        {
            (error, predictedDiseaseId) = await CheckAsync(result, model, cancellationToken);
        }

        // Read the case again: it may have been cancelled while the AI was working.
        var diagnosisCase = await LoadCaseAsync(caseId, cancellationToken);
        diagnosisCase.StartProcessing();
        var image = diagnosisCase.Images.Where(i => !i.IsDeleted).OrderByDescending(i => i.IsPrimary).First();
        var inferredAt = clock.UtcNow;

        if (error is not null || result is null)
        {
            var failureOutput = JsonSerializer.Serialize(new { error }, Json);
            diagnosisCase.RecordInference(
                image.Id, model, policy, FailedLabel, 0m, false, AiInferenceStatus.Failed, inferredAt,
                rawOutput: failureOutput);
            diagnosisCase.FailAi();
            logger.LogWarning("Diagnosis case {CaseNumber}: AI result refused ({Error}).", caseNumber, error);
        }
        else
        {
            var scores = result.TopPredictions
                .Select(t => new AiClassScore(t.ClassLabel, CostRounding.RoundUnitCost(t.Confidence)))
                .OrderByDescending(t => t.Confidence)
                .ToList();
            var confidence = CostRounding.RoundUnitCost(result.Confidence);
            var margin = CostRounding.RoundUnitCost(confidence - (scores.Count > 1 ? scores[1].Confidence : 0m));
            var passed = confidence >= policy.MinimumConfidence
                && (policy.MinimumMargin is null || margin >= policy.MinimumMargin);

            diagnosisCase.RecordInference(
                image.Id, model, policy, result.PredictedClassLabel, confidence, passed, AiInferenceStatus.Success, inferredAt,
                predictedDiseaseId,
                JsonSerializer.Serialize(scores.Select(s => new { classLabel = s.ClassLabel, confidence = s.Confidence }), Json),
                JsonSerializer.Serialize(new
                {
                    rawConfidence = result.RawConfidence,
                    modelName = result.ModelName,
                    modelVersion = result.ModelVersion,
                    stub = result.IsStub,
                    margin,
                    error = (string?)null
                }, Json),
                result.InferenceDurationMs);
            diagnosisCase.CompleteAi();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    // The answer is evidence only if it comes from the ACTIVE model, is not a simulation outside Development and names
    // one of the model's classes that is a known disease.
    private async Task<(string? Error, Guid? DiseaseId)> CheckAsync(
        AiPredictionResult result, Domain.Features.Diagnosis.Entities.AiModel model, CancellationToken cancellationToken)
    {
        if (!string.Equals(result.ModelVersion, model.Version, StringComparison.Ordinal))
        {
            return ("MODEL_VERSION_MISMATCH", null);
        }

        if (result.IsStub && !options.Value.AllowStubResults)
        {
            return ("STUB_REFUSED", null);
        }

        List<string>? modelLabels;
        try
        {
            modelLabels = JsonSerializer.Deserialize<List<string>>(model.ClassLabels);
        }
        catch (JsonException)
        {
            modelLabels = null;
        }

        var labels = result.TopPredictions.Select(t => t.ClassLabel).Append(result.PredictedClassLabel).Distinct().ToList();
        if (modelLabels is null || labels.Any(l => !modelLabels.Contains(l)))
        {
            return ("UNKNOWN_CLASS_LABEL", null);
        }

        var disease = await context.Diseases.AsNoTracking()
            .Where(d => d.Code == result.PredictedClassLabel)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return disease is null ? ("UNKNOWN_CLASS_LABEL", null) : (null, disease);
    }

    private async Task<Domain.Features.Diagnosis.Entities.DiagnosisCase> LoadCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        await context.DiagnosisCases
            .Include(c => c.Images)
            .Include(c => c.Inferences)
            .FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken)
        ?? throw new NotFoundException("Diagnosis case", caseId);
}
