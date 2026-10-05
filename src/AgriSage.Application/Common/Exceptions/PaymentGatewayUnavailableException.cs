namespace AgriSage.Application.Common.Exceptions;

// The online payment provider is not configured, unreachable or refused the request (HTTP 503).
// The message is shown to the client: it never carries provider details, keys or raw responses.
public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException(string message = "Online payment is currently unavailable.")
        : base(message)
    {
    }
}
