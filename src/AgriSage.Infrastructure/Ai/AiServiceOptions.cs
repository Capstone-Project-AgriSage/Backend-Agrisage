namespace AgriSage.Infrastructure.Ai;

// AI inference service settings ("AiService" section). BaseUrl and TimeoutSeconds are not secret; ApiKey comes only from
// User Secrets / environment variables / appsettings.Local.json and is never logged.
public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    // e.g. http://localhost:8001
    public string? BaseUrl { get; init; }

    // Sent as the X-Api-Key header; the AI service refuses /v1/* without it when it has a key configured.
    public string? ApiKey { get; init; }

    public int TimeoutSeconds { get; init; } = 15;

    // Real (default) calls the AI service; Simulated answers locally with deterministic made-up predictions.
    public string Mode { get; init; } = AiServiceMode.Real;

    // The model version a simulated answer reports. Register an AI model with this version to use the simulator.
    public string SimulatedModelVersion { get; init; } = "simulated";

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && TimeoutSeconds is > 0 and <= 120;
}
