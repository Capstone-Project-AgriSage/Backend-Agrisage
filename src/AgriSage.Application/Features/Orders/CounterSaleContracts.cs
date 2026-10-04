using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Orders;

// FLOW_1 §9: the quick counter sale. Source COUNTER, FULL_PAYMENT, PICKUP and a cash payment are implied.
public sealed record CounterSaleLotRequest(Guid InventoryLotId, long BaseQuantity);

// Lots are the ones actually handed over (required to sell, ignored by the preview); they add up to the line's base quantity.
public sealed record CounterSaleItemRequest(
    Guid StoreProductId,
    Guid ProductPackagingId,
    long Quantity,
    decimal? UnitPrice = null,
    string? OverrideReason = null,
    IReadOnlyList<CounterSaleLotRequest>? Lots = null);

public sealed record CounterSaleRequest(
    string CustomerType,
    IReadOnlyList<CounterSaleItemRequest> Items,
    Guid? FarmerProfileId = null,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? Note = null);

public sealed record CounterSalePreviewItem(
    Guid StoreProductId,
    Guid ProductPackagingId,
    string Sku,
    string ProductName,
    string PackagingName,
    long Quantity,
    long ConversionToBase,
    long BaseQuantity,
    decimal SuggestedUnitPrice,
    decimal UnitPrice,
    decimal LineTotalAmount,
    IReadOnlyList<FefoLotSuggestion> Lots,
    long ShortageBaseQuantity);

public sealed record CounterSalePreviewResponse(
    Guid? CustomerGroupId,
    Guid? PriceListId,
    decimal TotalAmount,
    IReadOnlyList<CounterSalePreviewItem> Items);

// The order is COMPLETED and the payment PAID.
public sealed record CounterSaleResponse(OrderResponse Order, PaymentResponse Payment);

public interface ICounterSaleService
{
    Task<CounterSalePreviewResponse> PreviewAsync(CounterSaleRequest request, CancellationToken cancellationToken);

    Task<CounterSaleResponse> SellAsync(CounterSaleRequest request, CancellationToken cancellationToken);
}

public sealed class CounterSaleRequestValidator : AbstractValidator<CounterSaleRequest>
{
    public const int MaxItems = 100;

    public CounterSaleRequestValidator()
    {
        RuleFor(r => r.CustomerType).Must(v => EnumText.TryParse<CustomerType>(v, out _))
            .WithMessage("CustomerType must be REGISTERED or WALK_IN.");
        RuleFor(r => r.CustomerName).MaximumLength(150);
        RuleFor(r => r.CustomerPhone).MaximumLength(20);
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleFor(r => r.FarmerProfileId).Must(id => id is { } value && value != Guid.Empty)
            .When(r => EnumText.TryParse<CustomerType>(r.CustomerType, out var type) && type == CustomerType.Registered)
            .WithMessage("A REGISTERED sale needs a farmerProfileId.");

        RuleFor(r => r.Items).NotEmpty()
            .Must(items => items is null || items.Count <= MaxItems).WithMessage($"At most {MaxItems} lines per sale.")
            .Must(items => items is null
                || items.Select(i => (i.StoreProductId, i.ProductPackagingId)).Distinct().Count() == items.Count)
            .WithMessage("Each (storeProductId, productPackagingId) pair may appear only once; add the quantities together.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.StoreProductId).NotEmpty();
            item.RuleFor(i => i.ProductPackagingId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity);
            item.RuleFor(i => i.UnitPrice!.Value).MustBeMoney().When(i => i.UnitPrice is not null).OverridePropertyName("UnitPrice");
            item.RuleFor(i => i.OverrideReason).MaximumLength(500);
            item.RuleFor(i => i.Lots)
                .Must(lots => lots is null || lots.Select(l => l.InventoryLotId).Distinct().Count() == lots.Count)
                .WithMessage("Each lot may appear only once per line.");
            item.RuleForEach(i => i.Lots).ChildRules(lot =>
            {
                lot.RuleFor(l => l.InventoryLotId).NotEmpty();
                lot.RuleFor(l => l.BaseQuantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity * 1000);
            }).When(i => i.Lots is not null);
        }).When(r => r.Items is not null);
    }
}
