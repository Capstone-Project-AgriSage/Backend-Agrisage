namespace AgriSage.Application.Common.Exceptions;

// Credentials are missing or wrong (HTTP 401). The message is shown to the client: keep it generic.
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message = "Invalid credentials.")
        : base(message)
    {
    }
}
