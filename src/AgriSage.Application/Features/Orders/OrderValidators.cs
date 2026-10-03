using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Orders;

public sealed class DeliveryAddressRequestValidator : AbstractValidator<DeliveryAddressRequest>
{
    public DeliveryAddressRequestValidator()
    {
        RuleFor(a => a.RecipientName).NotEmpty().MaximumLength(150);
        RuleFor(a => a.RecipientPhone).NotEmpty().MaximumLength(20);
        RuleFor(a => a.AddressLine).NotEmpty().MaximumLength(500);
        RuleFor(a => a.Ward).MaximumLength(150);
        RuleFor(a => a.District).MaximumLength(150);
        RuleFor(a => a.Province).NotEmpty().MaximumLength(150);
        RuleFor(a => a.Latitude).InclusiveBetween(-90m, 90m).When(a => a.Latitude is not null);
        RuleFor(a => a.Longitude).InclusiveBetween(-180m, 180m).When(a => a.Longitude is not null);
    }
}

public sealed class OrderItemRequestValidator : AbstractValidator<OrderItemRequest>
{
    // Keeps quantity × conversion far from the long range.
    public const long MaxQuantity = 100_000_000;

    public OrderItemRequestValidator()
    {
        RuleFor(i => i.StoreProductId).NotEmpty();
        RuleFor(i => i.ProductPackagingId).NotEmpty();
        RuleFor(i => i.Quantity).InclusiveBetween(1, MaxQuantity);
        RuleFor(i => i.UnitPrice!.Value).MustBeMoney().When(i => i.UnitPrice is not null).OverridePropertyName("UnitPrice");
        RuleFor(i => i.OverrideReason).MaximumLength(500);
    }
}

public sealed class CreateCounterOrderRequestValidator : AbstractValidator<CreateCounterOrderRequest>
{
    public const int MaxItems = 100;

    public CreateCounterOrderRequestValidator()
    {
        RuleFor(r => r.CustomerType).Must(v => EnumText.TryParse<CustomerType>(v, out _))
            .WithMessage("CustomerType must be REGISTERED or WALK_IN.");
        RuleFor(r => r.SettlementType).Must(v => EnumText.TryParse<SettlementType>(v, out _))
            .WithMessage("SettlementType must be FULL_PAYMENT or CREDIT.");
        RuleFor(r => r.FulfillmentType).Must(v => EnumText.TryParse<FulfillmentType>(v, out _))
            .WithMessage("FulfillmentType must be PICKUP or DELIVERY.");
        RuleFor(r => r.CustomerName).MaximumLength(150);
        RuleFor(r => r.CustomerPhone).MaximumLength(20);
        RuleFor(r => r.Note).MaximumLength(1000);

        RuleFor(r => r.Items).NotEmpty()
            .Must(items => items is null || items.Count <= MaxItems).WithMessage($"At most {MaxItems} lines per order.")
            .Must(items => items is null
                || items.Select(i => (i.StoreProductId, i.ProductPackagingId)).Distinct().Count() == items.Count)
            .WithMessage("Each (storeProductId, productPackagingId) pair may appear only once; add the quantities together.");
        RuleForEach(r => r.Items).SetValidator(new OrderItemRequestValidator()).When(r => r.Items is not null);
        RuleFor(r => r.DeliveryAddress!).SetValidator(new DeliveryAddressRequestValidator()).When(r => r.DeliveryAddress is not null);

        RuleFor(r => r.FarmerProfileId).NotEmpty()
            .When(r => EnumText.TryParse<CustomerType>(r.CustomerType, out var type) && type == CustomerType.Registered)
            .WithMessage("A REGISTERED order needs a farmerProfileId.");
        RuleFor(r => r).Must(r => (r.AddressId is null) != (r.DeliveryAddress is null))
            .When(r => EnumText.TryParse<FulfillmentType>(r.FulfillmentType, out var type) && type == FulfillmentType.Delivery)
            .WithName("Address").WithMessage("A DELIVERY order needs exactly one of addressId or deliveryAddress.");
        RuleFor(r => r).Must(r => r.AddressId is null && r.DeliveryAddress is null)
            .When(r => EnumText.TryParse<FulfillmentType>(r.FulfillmentType, out var type) && type == FulfillmentType.Pickup)
            .WithName("Address").WithMessage("A PICKUP order has no delivery address.");
    }
}

public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.DeliveryAddress!).SetValidator(new DeliveryAddressRequestValidator()).When(r => r.DeliveryAddress is not null);
        RuleFor(r => r).Must(r => r.AddressId is null || r.DeliveryAddress is null)
            .WithName("Address").WithMessage("Send either addressId or deliveryAddress, not both.");
    }
}

public sealed class ChangeOrderItemQuantityRequestValidator : AbstractValidator<ChangeOrderItemQuantityRequest>
{
    public ChangeOrderItemQuantityRequestValidator() =>
        RuleFor(r => r.Quantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
}

public sealed class OverrideOrderItemPriceRequestValidator : AbstractValidator<OverrideOrderItemPriceRequest>
{
    public OverrideOrderItemPriceRequestValidator()
    {
        RuleFor(r => r.UnitPrice).MustBeMoney();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class OrderListRequestValidator : AbstractValidator<OrderListRequest>
{
    public OrderListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(v => EnumText.TryParse<OrderStatus>(v, out _)).When(r => !string.IsNullOrWhiteSpace(r.Status))
            .WithMessage("Unknown order status.");
        RuleFor(r => r.CustomerType).Must(v => EnumText.TryParse<CustomerType>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.CustomerType)).WithMessage("CustomerType must be REGISTERED or WALK_IN.");
        RuleFor(r => r.SettlementType).Must(v => EnumText.TryParse<SettlementType>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.SettlementType)).WithMessage("SettlementType must be FULL_PAYMENT or CREDIT.");
        RuleFor(r => r.FulfillmentType).Must(v => EnumText.TryParse<FulfillmentType>(v, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.FulfillmentType)).WithMessage("FulfillmentType must be PICKUP or DELIVERY.");
        RuleFor(r => r.Source).Must(v => EnumText.TryParse<OrderSource>(v, out _)).When(r => !string.IsNullOrWhiteSpace(r.Source))
            .WithMessage("Source must be FARMER_WEB, FARMER_MOBILE or COUNTER.");
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate!.Value)
            .When(r => r.FromDate is not null && r.ToDate is not null).WithMessage("ToDate must not be before FromDate.");
        RuleFor(r => r.Search).MaximumLength(100);
    }
}
