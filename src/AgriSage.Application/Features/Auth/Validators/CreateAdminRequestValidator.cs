using AgriSage.Application.Common;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using FluentValidation;

namespace AgriSage.Application.Features.Auth.Validators;

public sealed class CreateAdminRequestValidator : AbstractValidator<CreateAdminRequest>
{
    public CreateAdminRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(150);
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(ContactNormalizer.MaxEmailLength);

        RuleFor(request => request.PhoneNumber)
            .Must(phone => ContactNormalizer.TryNormalizePhone(phone, out _))
            .WithMessage("Phone number must be a valid Vietnamese mobile number.")
            .When(request => !string.IsNullOrWhiteSpace(request.PhoneNumber));

        RuleFor(request => request.Password).MustBeValidPassword();
    }
}
