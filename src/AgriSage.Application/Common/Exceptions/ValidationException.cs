namespace AgriSage.Application.Common.Exceptions;

// Note: distinct from FluentValidation.ValidationException; alias one of them when both namespaces are imported.
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
