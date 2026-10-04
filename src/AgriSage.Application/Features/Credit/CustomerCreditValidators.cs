using FluentValidation;

namespace AgriSage.Application.Features.Credit;

internal static class CreditMoney
{
    internal static bool Valid(decimal value) => value >= 0 && value <= 9999999999999999.99m && decimal.Round(value, 2) == value;
}
public sealed class CreateCustomerCreditRequestValidator : AbstractValidator<CreateCustomerCreditRequest>
{
    public CreateCustomerCreditRequestValidator()
    {
        RuleFor(r => r.CreditTierId).NotEqual(Guid.Empty);
        RuleFor(r => r.CreditLimit).Must(v => v is null || CreditMoney.Valid(v.Value));
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
public sealed class CustomerCreditLimitRequestValidator : AbstractValidator<CustomerCreditLimitRequest>
{
    public CustomerCreditLimitRequestValidator()
    {
        RuleFor(r => r.CreditTierId).NotEqual(Guid.Empty);
        RuleFor(r => r.CreditLimit).Must(CreditMoney.Valid);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
    }
}
public sealed class CustomerCreditStatusRequestValidator : AbstractValidator<CustomerCreditStatusRequest>
{
    public CustomerCreditStatusRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000);
}
public sealed class CreditTierRequestValidator : AbstractValidator<CreditTierRequest>
{
    public CreditTierRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(30);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.DefaultCreditLimit).Must(CreditMoney.Valid);
        RuleFor(r => r.DefaultPaymentTermDays).GreaterThanOrEqualTo(0);
    }
}
public sealed class UpdateCreditTierRequestValidator : AbstractValidator<UpdateCreditTierRequest>
{
    public UpdateCreditTierRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.DefaultCreditLimit).Must(CreditMoney.Valid);
        RuleFor(r => r.DefaultPaymentTermDays).GreaterThanOrEqualTo(0);
    }
}
