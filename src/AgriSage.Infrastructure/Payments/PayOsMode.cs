using Microsoft.Extensions.Configuration;

namespace AgriSage.Infrastructure.Payments;

// Which payment gateway the API runs with: PayOS:Mode = Real (default, the payOS adapter) or Simulated (in-memory, no money,
// see SimulatedPaymentGateway). An unknown value is a startup error rather than a silent fallback.
public static class PayOsMode
{
    public const string Real = "Real";

    public const string Simulated = "Simulated";

    public static bool IsSimulated(IConfiguration configuration)
    {
        var mode = configuration[$"{PayOsOptions.SectionName}:Mode"];
        if (string.IsNullOrWhiteSpace(mode) || mode.Trim().Equals(Real, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (mode.Trim().Equals(Simulated, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException($"PayOS:Mode must be '{Real}' or '{Simulated}', not '{mode}'.");
    }

    // The simulated gateway confirms payments without any money: it may only run in the Development environment.
    public static void EnsureAllowed(bool simulated, bool isDevelopment)
    {
        if (simulated && !isDevelopment)
        {
            throw new InvalidOperationException(
                $"PayOS:Mode={Simulated} moves no money and is allowed only in the Development environment. Use PayOS:Mode={Real}.");
        }
    }
}
