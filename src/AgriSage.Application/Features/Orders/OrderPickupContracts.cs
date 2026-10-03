using AgriSage.Application.Common.Validators;
using FluentValidation;

namespace AgriSage.Application.Features.Orders;

// FLOW_1 §7. The lots are the ones the staff actually handed over, in base units.
public sealed record PickupLotRequest(Guid InventoryLotId, long BaseQuantity);

public sealed record PickupItemRequest(Guid OrderItemId, IReadOnlyList<PickupLotRequest> Lots);

public sealed record PickupRequest(IReadOnlyList<PickupItemRequest> Items, string? Note = null);

public sealed record CancelRemainingRequest(string Reason);

public interface IOrderPickupService
{
    Task<OrderResponse> PickupAsync(Guid orderId, PickupRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> CancelRemainingAsync(Guid orderId, Guid itemId, CancelRemainingRequest request, CancellationToken cancellationToken);
}

public sealed class PickupRequestValidator : AbstractValidator<PickupRequest>
{
    public const int MaxItems = 100;

    public PickupRequestValidator()
    {
        RuleFor(r => r.Items).NotEmpty()
            .Must(items => items is null || items.Count <= MaxItems).WithMessage($"At most {MaxItems} items per pickup.")
            .Must(items => items is null || items.Select(i => i.OrderItemId).Distinct().Count() == items.Count)
            .WithMessage("Each order item may appear only once; list all its lots together.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.OrderItemId).NotEmpty();
            item.RuleFor(i => i.Lots).NotEmpty()
                .Must(lots => lots is null || lots.Select(l => l.InventoryLotId).Distinct().Count() == lots.Count)
                .WithMessage("Each lot may appear only once per item.");
            item.RuleForEach(i => i.Lots).ChildRules(lot =>
            {
                lot.RuleFor(l => l.InventoryLotId).NotEmpty();
                lot.RuleFor(l => l.BaseQuantity).InclusiveBetween(1, OrderItemRequestValidator.MaxQuantity * 1000);
            }).When(i => i.Lots is not null);
        }).When(r => r.Items is not null);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class CancelRemainingRequestValidator : AbstractValidator<CancelRemainingRequest>
{
    public CancelRemainingRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
}
