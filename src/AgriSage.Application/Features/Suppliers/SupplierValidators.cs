using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Suppliers;

public sealed class SupplierRequestValidator : AbstractValidator<SupplierRequest>
{
    public SupplierRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Code).MaximumLength(50);
        RuleFor(r => r.TaxCode).MaximumLength(50);
        RuleFor(r => r.PhoneNumber).MaximumLength(20);
        RuleFor(r => r.Email).EmailAddress().MaximumLength(ContactNormalizer.MaxEmailLength)
            .When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.ContactPerson).MaximumLength(150);
        RuleFor(r => r.AddressLine).MaximumLength(500);
        RuleFor(r => r.Ward).MaximumLength(150);
        RuleFor(r => r.District).MaximumLength(150);
        RuleFor(r => r.Province).MaximumLength(150);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class SupplierListRequestValidator : AbstractValidator<SupplierListRequest>
{
    public SupplierListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
    }
}
