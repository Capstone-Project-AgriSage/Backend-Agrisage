namespace AgriSage.Application.Common.Exceptions;

// Use-case level rule violations (e.g. rules needing DB state or several entities).
// Single-entity invariants throw AgriSage.Domain.Common.Exceptions.DomainException instead.
// Errors (optional) lists the offending items when there are several, e.g. "row 3" → messages of an Excel import;
// it is returned as `errors` in the 422 problem details.
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}
