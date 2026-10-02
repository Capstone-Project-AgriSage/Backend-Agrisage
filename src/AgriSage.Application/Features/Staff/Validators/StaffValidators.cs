using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Validators;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Domain.Features.Identity.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Staff.Validators;

internal static class StaffRules
{
    public static bool IsStaffRoleText(string? text) =>
        RoleCodeFormat.TryParse(text, out var role) && StaffPolicy.IsStaffRole(role);

    public static void ContactRules<T>(AbstractValidator<T> validator, Func<T, string?> phone, Func<T, string?> email)
    {
        validator.RuleFor(request => phone(request))
            .Must(value => ContactNormalizer.TryNormalizePhone(value, out _))
            .WithName("phoneNumber")
            .WithMessage("Phone number must be a valid Vietnamese mobile number.")
            .When(request => !string.IsNullOrWhiteSpace(phone(request)));

        validator.RuleFor(request => email(request))
            .EmailAddress().MaximumLength(ContactNormalizer.MaxEmailLength)
            .WithName("email")
            .When(request => !string.IsNullOrWhiteSpace(email(request)));

        validator.RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(phone(request)) || !string.IsNullOrWhiteSpace(email(request)))
            .WithName("contact")
            .WithMessage("A phone number or an email is required.");
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(150);
        RuleFor(request => request.Role).Must(StaffRules.IsStaffRoleText)
            .WithMessage("Role must be STORE_OWNER, SALES_STAFF or DELIVERY_STAFF.");
        StaffRules.ContactRules(this, request => request.PhoneNumber, request => request.Email);
        RuleFor(request => request.Password).MustBeValidPassword();
        RuleFor(request => request.EmployeeCode).MaximumLength(50);
    }
}

public sealed class UpdateStaffRequestValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffRequestValidator()
    {
        RuleFor(request => request.FullName).NotEmpty().MaximumLength(150);
        StaffRules.ContactRules(this, request => request.PhoneNumber, request => request.Email);
        RuleFor(request => request.EmployeeCode).MaximumLength(50);
    }
}

public sealed class ResetStaffPasswordRequestValidator : AbstractValidator<ResetStaffPasswordRequest>
{
    public ResetStaffPasswordRequestValidator()
    {
        RuleFor(request => request.NewPassword).MustBeValidPassword();
    }
}

public sealed class StaffListRequestValidator : AbstractValidator<StaffListRequest>
{
    public StaffListRequestValidator()
    {
        Include(new PaginationRequestValidator());

        RuleFor(request => request.Role).Must(StaffRules.IsStaffRoleText)
            .WithMessage("Role must be STORE_OWNER, SALES_STAFF or DELIVERY_STAFF.")
            .When(request => !string.IsNullOrWhiteSpace(request.Role));

        RuleFor(request => request.Status).Must(status => Enum.TryParse<UserStatus>(status, ignoreCase: true, out _))
            .WithMessage("Status must be ACTIVE, INACTIVE, SUSPENDED or LOCKED.")
            .When(request => !string.IsNullOrWhiteSpace(request.Status));

        RuleFor(request => request.Search).MaximumLength(100);
    }
}
