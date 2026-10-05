using AgriSage.Application.Features.Orders;
using FluentValidation;

namespace AgriSage.Application.Features.Carts;

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(r => r.StoreProductId).NotEmpty();
        RuleFor(r => r.ProductPackagingId).NotEmpty();
        RuleFor(r => r.Quantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator() =>
        RuleFor(r => r.Quantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
}
