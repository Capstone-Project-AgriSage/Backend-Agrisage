using AgriSage.Application.Common;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using FluentValidation;

namespace AgriSage.Application.Features.Auth.Validators;

public sealed class RegisterFarmerRequestValidator : AbstractValidator<RegisterFarmerRequest>
{
    public RegisterFarmerRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(150);

        RuleFor(request => request.PhoneNumber)
            .Must(phone => ContactNormalizer.TryNormalizePhone(phone, out _))
            .WithMessage("Phone number must be a valid Vietnamese mobile number.")
            .When(request => !string.IsNullOrWhiteSpace(request.PhoneNumber));

        RuleFor(request => request.Email)
            .EmailAddress().MaximumLength(ContactNormalizer.MaxEmailLength)
            .When(request => !string.IsNullOrWhiteSpace(request.Email));

        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.PhoneNumber) || !string.IsNullOrWhiteSpace(request.Email))
            .WithName("contact")
            .WithMessage("A phone number or an email is required.");

        RuleFor(request => request.Password).MustBeValidPassword();
    }
}
