using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Customers;

// Lengths follow user_addresses / users / farmer_profiles (design §2–4); coordinates are numeric(10,7).
public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(a => a.RecipientName).NotEmpty().MaximumLength(150);
        RuleFor(a => a.RecipientPhone).NotEmpty().MaximumLength(20)
            .Must(p => ContactNormalizer.TryNormalizePhone(p, out _))
            .WithMessage("The recipient phone must be a valid Vietnamese mobile number.");
        RuleFor(a => a.AddressLine).NotEmpty().MaximumLength(500);
        RuleFor(a => a.Ward).MaximumLength(150);
        RuleFor(a => a.District).MaximumLength(150);
        RuleFor(a => a.Province).NotEmpty().MaximumLength(150);
        RuleFor(a => a.AddressType).Must(t => EnumText.TryParse<AddressType>(t, out _))
            .WithMessage("addressType must be HOME, FARM or OTHER.");
        RuleFor(a => a.Latitude).InclusiveBetween(-90m, 90m).Must(HaveAtMostSevenDecimals).When(a => a.Latitude is not null);
        RuleFor(a => a.Longitude).InclusiveBetween(-180m, 180m).Must(HaveAtMostSevenDecimals).When(a => a.Longitude is not null);
    }

    private static bool HaveAtMostSevenDecimals(decimal? value) => value is null || decimal.Round(value.Value, 7) == value;
}

public sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    private static readonly string[] Genders = ["MALE", "FEMALE", "OTHER"];

    public UpdateMyProfileRequestValidator(IDateTimeProvider clock)
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Gender).Must(g => Genders.Contains(g!.Trim().ToUpperInvariant()))
            .When(r => !string.IsNullOrWhiteSpace(r.Gender))
            .WithMessage("gender must be MALE, FEMALE or OTHER.");
        RuleFor(r => r.DateOfBirth).Must(d => d!.Value.Year >= 1900 && d.Value <= BusinessCalendar.Today(clock.UtcNow))
            .When(r => r.DateOfBirth is not null)
            .WithMessage("dateOfBirth must be a past date after 1900.");
    }
}
