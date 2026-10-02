using FluentValidation;

namespace AgriSage.Application.Features.Auth.Validators;

public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    // Length matters more than composition (NIST); no composition rule so farmers can remember it.
    public static IRuleBuilderOptions<T, string> MustBeValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(MinLength).MaximumLength(MaxLength);
}
