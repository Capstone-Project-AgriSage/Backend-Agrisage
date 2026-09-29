namespace AgriSage.Application.Common.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} '{key}' was not found.")
    {
    }
}
