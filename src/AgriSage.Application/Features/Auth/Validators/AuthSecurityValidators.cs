using AgriSage.Application.Features.Auth.Dtos.Requests;
using FluentValidation;

namespace AgriSage.Application.Features.Auth.Validators;

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().Length(43).Matches("^[A-Za-z0-9_-]+$");
}
public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(r => r.Identifier).NotEmpty().MaximumLength(255);
}
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().Length(76).Matches("^[a-fA-F0-9]{32}\\.[A-Za-z0-9_-]{43}$");
        RuleFor(r => r.NewPassword).MustBeValidPassword();
    }
}
public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator() => RuleFor(r => r.Token).NotEmpty().Length(76)
        .Matches("^[a-fA-F0-9]{32}\\.[A-Za-z0-9_-]{43}$");
}
public sealed class ConfirmPhoneRequestValidator : AbstractValidator<ConfirmPhoneRequest>
{
    public ConfirmPhoneRequestValidator() => RuleFor(r => r.Code).NotEmpty().Matches("^[0-9]{6}$");
}
