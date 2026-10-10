namespace AgriSage.Application.Common.Exceptions;

// The AI inference service is not configured, unreachable, timed out or answered with something unusable (HTTP 503).
// The message is shown to the client: it never carries provider details, URLs or keys.
public sealed class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message = "AI diagnosis is currently unavailable.")
        : base(message)
    {
    }
}
