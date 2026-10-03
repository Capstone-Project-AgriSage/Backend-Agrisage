using FluentValidation;

namespace AgriSage.Application.Common.Validators;

public static class MoneyRules
{
    // Money is ≥ 0 with at most 2 decimals and is never rounded (coding rule #61).
    public static IRuleBuilderOptions<T, decimal> MustBeMoney<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0)
            .Must(value => value == decimal.Round(value, 2))
            .WithMessage("An amount can have at most 2 decimal places.");
}
