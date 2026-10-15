using Microsoft.Extensions.Configuration;

namespace AgriSage.Infrastructure.Messaging;

// Whether the API runs with the simulated SMS provider: MessageDelivery:SmsProvider = Simulated (no SMS leaves the machine,
// nothing is charged, the code shows in the simulated outbox). A value that is not a known provider is rejected by the
// options validation at startup, so only the Development rule is checked here.
public static class SimulatedSmsMode
{
    public static bool IsSimulated(IConfiguration configuration) =>
        string.Equals(configuration[$"MessageDelivery:{nameof(MessageDeliveryOptions.SmsProvider)}"]?.Trim(),
            MessageDeliveryOptions.SimulatedSmsProvider, StringComparison.OrdinalIgnoreCase);

    // The simulated provider shows verification codes in an anonymous page: it may only run in Development.
    public static void EnsureAllowed(bool simulated, bool isDevelopment)
    {
        if (simulated && !isDevelopment)
        {
            throw new InvalidOperationException(
                $"MessageDelivery:SmsProvider={MessageDeliveryOptions.SimulatedSmsProvider} sends no SMS and shows the codes in a " +
                "page anyone can open: it is allowed only in the Development environment.");
        }
    }
}
