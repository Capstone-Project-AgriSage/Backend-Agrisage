namespace AgriSage.Application.Common.Interfaces;

public sealed record AiPredictionRequest(Stream Image, string FileName, string ContentType, int TopK);

public sealed record AiPredictionResult(
    string PredictedClassLabel,
    decimal Confidence,
    IReadOnlyList<AiClassScore> TopPredictions,
    string ModelVersion,
    int? InferenceDurationMs);

public sealed record AiClassScore(string ClassLabel, decimal Confidence);
