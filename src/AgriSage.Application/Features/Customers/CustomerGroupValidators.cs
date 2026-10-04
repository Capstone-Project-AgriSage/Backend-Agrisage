using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Customers;

public sealed class CustomerGroupRequestValidator : AbstractValidator<CustomerGroupRequest>
{
    public CustomerGroupRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(30);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
    }
}
public sealed class UpdateCustomerGroupRequestValidator : AbstractValidator<UpdateCustomerGroupRequest>
{
    public UpdateCustomerGroupRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
    }
}
public sealed class CustomerGroupListRequestValidator : AbstractValidator<CustomerGroupListRequest>
{
    public CustomerGroupListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
    }
}
public sealed class GroupCreditTierRequestValidator : AbstractValidator<GroupCreditTierRequest>
{
    public GroupCreditTierRequestValidator() => RuleFor(r => r.CreditTierId).NotEqual(Guid.Empty);
}
public sealed class GroupPriceListRequestValidator : AbstractValidator<GroupPriceListRequest>
{
    public GroupPriceListRequestValidator() => RuleFor(r => r.PriceListId).NotEmpty();
}
