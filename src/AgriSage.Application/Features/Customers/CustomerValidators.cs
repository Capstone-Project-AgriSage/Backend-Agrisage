using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Auth.Validators;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Customers;

public class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.PhoneNumber).Must(p => ContactNormalizer.TryNormalizePhone(p, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.PhoneNumber))
            .WithMessage("Phone number must be a valid Vietnamese mobile number.");
        RuleFor(r => r.Email).EmailAddress().MaximumLength(255).When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.PhoneNumber) || !string.IsNullOrWhiteSpace(r.Email))
            .WithName("contact").WithMessage("A phone number or an email is required.");
        RuleFor(r => r.Notes).MaximumLength(1000);
        RuleFor(r => r.CustomerGroupId).NotEqual(Guid.Empty);
        RuleFor(r => r.CreditTierId).NotEqual(Guid.Empty);
        RuleFor(r => r.CreditLimit).Must(v => v is null || (v >= 0 && v <= 9999999999999999.99m && decimal.Round(v.Value, 2) == v))
            .WithMessage("Credit limit must be nonnegative money with at most two decimals.");
        RuleFor(r => r.CreditChangeReason).MaximumLength(1000);
        RuleFor(r => r).Must(r => r.AllowCreditPurchase != false || (r.CreditLimit is null && r.CreditTierId is null))
            .WithName("creditLimit").WithMessage("Credit settings cannot be changed while disabling credit purchases.");
        RuleFor(r => r.Address).SetValidator(new CustomerAddressRequestValidator()!).When(r => r.Address != null);
    }
}

public sealed class CustomerAddressRequestValidator : AbstractValidator<CustomerAddressRequest>
{
    public CustomerAddressRequestValidator()
    {
        RuleFor(r => r.RecipientName).NotEmpty().MaximumLength(150);
        RuleFor(r => r.RecipientPhone).Must(p => ContactNormalizer.TryNormalizePhone(p, out _));
        RuleFor(r => r.AddressLine).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Province).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Ward).MaximumLength(100);
        RuleFor(r => r.District).MaximumLength(100);
    }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        Include(new CustomerRequestValidator());
        RuleFor(r => r.Password).MustBeValidPassword();
        RuleFor(r => r.CustomerType).Must(s => string.Equals(s, "REGISTERED", StringComparison.OrdinalIgnoreCase))
            .WithMessage("WALK_IN customers are recorded on counter orders, not as Farmer profiles.");
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator() => Include(new CustomerRequestValidator());
}

public sealed class CustomerStatusRequestValidator : AbstractValidator<CustomerStatusRequest>
{
    public CustomerStatusRequestValidator() => RuleFor(r => r.Status).Must(s => EnumText.TryParse<UserStatus>(s, out _));
}

public sealed class AssignCustomerGroupRequestValidator : AbstractValidator<AssignCustomerGroupRequest>
{
    public AssignCustomerGroupRequestValidator()
    {
        RuleFor(r => r.CustomerGroupId).NotEmpty();
        RuleFor(r => r.Reason).MaximumLength(500);
    }
}

public sealed class CustomerListRequestValidator : AbstractValidator<CustomerListRequest>
{
    public CustomerListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(150);
        RuleFor(r => r.CustomerGroupId).NotEqual(Guid.Empty);
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<UserStatus>(s, out _)).When(r => r.Status != null);
        RuleFor(r => r.CustomerType).Must(s => EnumText.TryParse<CustomerType>(s, out _)).When(r => r.CustomerType != null);
        RuleFor(r => r.SortBy).Must(s => new[] { "NAME", "CREATED_AT", "TOTAL_ORDERS", "CURRENT_DEBT" }.Contains(s?.ToUpperInvariant()));
    }
}

public sealed class CustomerOrderListRequestValidator : AbstractValidator<CustomerOrderListRequest>
{
    public CustomerOrderListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(50);
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<OrderStatus>(s, out _)).When(r => r.Status != null);
        RuleFor(r => r.PaymentStatus).Must(s => new[] { "UNPAID", "PARTIALLY_PAID", "PAID" }.Contains(s?.ToUpperInvariant()))
            .When(r => r.PaymentStatus != null);
        RuleFor(r => r.ToDate).Must((r, d) => d is null || d < DateOnly.MaxValue && (r.FromDate is null || d >= r.FromDate));
    }
}
