using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Orders;

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.Source).Must(v => EnumText.TryParse<OrderSource>(v, out var source) && source != OrderSource.Counter)
            .WithMessage("Source must be FARMER_WEB or FARMER_MOBILE.");
        RuleFor(r => r.SettlementType).Must(v => EnumText.TryParse<SettlementType>(v, out _))
            .WithMessage("SettlementType must be FULL_PAYMENT or CREDIT.");
        RuleFor(r => r.FulfillmentType).Must(v => EnumText.TryParse<FulfillmentType>(v, out _))
            .WithMessage("FulfillmentType must be PICKUP or DELIVERY.");
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.AddressId).NotEqual(Guid.Empty);
        RuleFor(r => r.DeliveryAddress!).SetValidator(new DeliveryAddressRequestValidator()).When(r => r.DeliveryAddress is not null);
        RuleFor(r => r).Must(r => (r.AddressId is null) != (r.DeliveryAddress is null))
            .When(r => EnumText.TryParse<FulfillmentType>(r.FulfillmentType, out var type) && type == FulfillmentType.Delivery)
            .WithName("Address").WithMessage("A DELIVERY order needs exactly one of addressId or deliveryAddress.");
        RuleFor(r => r).Must(r => r.AddressId is null && r.DeliveryAddress is null)
            .When(r => EnumText.TryParse<FulfillmentType>(r.FulfillmentType, out var type) && type == FulfillmentType.Pickup)
            .WithName("Address").WithMessage("A PICKUP order has no delivery address.");
    }
}

public sealed class MyOrderListRequestValidator : AbstractValidator<MyOrderListRequest>
{
    public MyOrderListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<OrderStatus>(s, out _)).When(r => r.Status is not null)
            .WithMessage("Unknown order status.");
        RuleFor(r => r.ToDate).Must((r, to) => to is null || r.FromDate is null || to >= r.FromDate)
            .WithMessage("toDate must not be before fromDate.");
    }
}

public sealed class CancelMyOrderRequestValidator : AbstractValidator<CancelMyOrderRequest>
{
    public CancelMyOrderRequestValidator() => RuleFor(r => r.Reason).MaximumLength(1000);
}
