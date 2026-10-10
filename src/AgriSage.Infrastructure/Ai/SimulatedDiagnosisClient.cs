using System.Security.Cryptography;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Ai;

// Development only (AiServiceMode.EnsureAllowed): answers without calling the AI service, so the Farmer and reviewer
// flows can be built and demonstrated before a model exists. The answer depends only on the photo's bytes, so the same
// photo always gets the same prediction. Every answer is flagged IsStub; it is never a diagnosis.
public sealed class SimulatedDiagnosisClient(IOptions<AiServiceOptions> options) : IAiDiagnosisClient
{
    private static readonly string[] Labels =
        ["LEAF_BLAST", "BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "SHEATH_BLIGHT", "HEALTHY"];

    public async Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Image.CopyToAsync(buffer, cancellationToken);
        var hash = SHA256.HashData(buffer.ToArray());

        // Five weights from the hash; the largest wins. Probabilities from a softmax of those weights.
        var logits = Enumerable.Range(0, Labels.Length).Select(i => hash[i] / 255.0 * 4.0).ToArray();
        var max = logits.Max();
        var exp = logits.Select(l => Math.Exp(l - max)).ToArray();
        var sum = exp.Sum();
        var scores = Labels
            .Select((label, i) => new AiClassScore(label, Math.Round((decimal)(exp[i] / sum), 6)))
            .OrderByDescending(s => s.Confidence)
            .ToList();
        var top = scores.Take(Math.Clamp(request.TopK, 1, Labels.Length)).ToList();

        return new AiPredictionResult(
            scores[0].ClassLabel,
            scores[0].Confidence,
            top,
            options.Value.SimulatedModelVersion,
            InferenceDurationMs: 5,
            RawConfidence: scores[0].Confidence,
            ModelName: "agrisage-simulated",
            IsStub: true);
    }
}
