namespace AgriSage.Domain.Common.Exceptions;

// Thrown when an entity invariant or state transition rule is violated.
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
