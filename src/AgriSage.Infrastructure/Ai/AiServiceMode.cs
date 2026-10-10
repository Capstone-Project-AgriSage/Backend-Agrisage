using Microsoft.Extensions.Configuration;

namespace AgriSage.Infrastructure.Ai;

// Which AI client the API runs with: AiService:Mode = Real (default, the FastAPI service) or Simulated (no call, made-up
// predictions). An unknown value is a startup error rather than a silent fallback.
public static class AiServiceMode
{
    public const string Real = "Real";

    public const string Simulated = "Simulated";

    public static bool IsSimulated(IConfiguration configuration)
    {
        var mode = configuration[$"{AiServiceOptions.SectionName}:Mode"];
        if (string.IsNullOrWhiteSpace(mode) || mode.Trim().Equals(Real, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (mode.Trim().Equals(Simulated, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException($"AiService:Mode must be '{Real}' or '{Simulated}', not '{mode}'.");
    }

    // Simulated predictions are not diagnoses: they may only run in the Development environment.
    public static void EnsureAllowed(bool simulated, bool isDevelopment)
    {
        if (simulated && !isDevelopment)
        {
            throw new InvalidOperationException(
                $"AiService:Mode={Simulated} produces made-up predictions and is allowed only in the Development environment. Use AiService:Mode={Real}.");
        }
    }
}
