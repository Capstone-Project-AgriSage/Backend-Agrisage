namespace AgriSage.Application.Common.Interfaces;

// CaseReference only labels the call in the AI service's logs (the case number); the service never stores the photo.
public sealed record AiPredictionRequest(Stream Image, string FileName, string ContentType, int TopK, string? CaseReference = null);

// Confidence is the CALIBRATED probability of the predicted class; the policy (minimum confidence / margin) is applied
// by the backend, never by the AI service. RawConfidence, ModelName and IsStub are evidence kept in raw_output.
public sealed record AiPredictionResult(
    string PredictedClassLabel,
    decimal Confidence,
    IReadOnlyList<AiClassScore> TopPredictions,
    string ModelVersion,
    int? InferenceDurationMs,
    decimal? RawConfidence = null,
    string? ModelName = null,
    bool IsStub = false);

public sealed record AiClassScore(string ClassLabel, decimal Confidence);
