using System.Runtime.CompilerServices;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Common;

// Entity invariant checks that throw DomainException.
internal static class Guard
{
    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        return value;
    }

    public static decimal NotNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
        {
            throw new DomainException($"{name} must not be negative.");
        }

        return value;
    }

    public static long Positive(
        long value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
        {
            throw new DomainException($"{name} must be greater than zero.");
        }

        return value;
    }

    public static long NotNegative(
        long value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
        {
            throw new DomainException($"{name} must not be negative.");
        }

        return value;
    }

    // Money (numeric(18,2)) is never rounded: more than 2 decimal places is rejected (coding rule #61).
    public static decimal Money(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value != Math.Round(value, 2))
        {
            throw new DomainException($"{name} must not have more than 2 decimal places.");
        }

        return value;
    }

    public static decimal NonNegativeMoney(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null) =>
        NotNegative(Money(value, name), name);

    public static decimal PositiveMoney(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (Money(value, name) <= 0)
        {
            throw new DomainException($"{name} must be greater than zero.");
        }

        return value;
    }

    // Unit cost (numeric(20,6)) supplied by a caller must already fit 6 decimal places.
    public static decimal UnitCost(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value != Math.Round(value, CostRounding.UnitCostDecimals))
        {
            throw new DomainException($"{name} must not have more than {CostRounding.UnitCostDecimals} decimal places.");
        }

        return NotNegative(value, name);
    }

    public static void ValidPeriod(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new DomainException("Effective end must be later than effective start.");
        }
    }
}
