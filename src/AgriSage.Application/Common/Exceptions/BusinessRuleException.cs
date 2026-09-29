namespace AgriSage.Application.Common.Exceptions;

// Use-case level rule violations (e.g. rules needing DB state or several entities).
// Single-entity invariants throw AgriSage.Domain.Common.Exceptions.DomainException instead.
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }
}
