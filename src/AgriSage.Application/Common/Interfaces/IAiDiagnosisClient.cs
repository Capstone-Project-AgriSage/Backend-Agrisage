namespace AgriSage.Application.Common.Interfaces;

// AI inference service abstraction (Python FastAPI). Results are not authoritative until Human Review.
public interface IAiDiagnosisClient
{
    Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken);
}
