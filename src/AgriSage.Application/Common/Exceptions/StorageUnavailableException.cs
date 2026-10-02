namespace AgriSage.Application.Common.Exceptions;

// The object storage provider is not configured, unreachable or rejected the request (HTTP 503).
// The message is shown to the client: it never carries provider details, URLs or keys.
public sealed class StorageUnavailableException : Exception
{
    public StorageUnavailableException(string message = "File storage is currently unavailable.")
        : base(message)
    {
    }
}
