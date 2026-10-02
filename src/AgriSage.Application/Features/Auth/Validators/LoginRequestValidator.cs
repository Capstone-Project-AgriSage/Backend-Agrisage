using AgriSage.Application.Features.Auth.Dtos.Requests;
using FluentValidation;

namespace AgriSage.Application.Features.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Identifier).NotEmpty().MaximumLength(255);

        // No minimum here: the login must not reveal the password policy or reject old passwords.
        RuleFor(request => request.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}
