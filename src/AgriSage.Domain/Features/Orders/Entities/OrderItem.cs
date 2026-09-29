using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Orders.Entities;

// Authoritative commercial snapshot of one ordered packaging. Changed only through Order.
// Invariant: fulfilled + cancelled <= base quantity. Status is derived (database design §35.4).
public sealed class OrderItem : SoftDeletableChildEntity
{
    private OrderItem()
    {
    }

    internal OrderItem(
        Guid orderId,
        Guid storeProductId,
        Guid productPackagingId,
        string productSkuSnapshot,
        string productNameSnapshot,
        string packagingNameSnapshot,
        long quantity,
        long conversionToBaseSnapshot,
        decimal suggestedUnitPrice,
        PriceOverride? priceOverride)
    {
        OrderId = orderId;
        StoreProductId = storeProductId;
        ProductPackagingId = productPackagingId;
        ProductSkuSnapshot = Guard.NotNullOrWhiteSpace(productSkuSnapshot);
        ProductNameSnapshot = Guard.NotNullOrWhiteSpace(productNameSnapshot);
        PackagingNameSnapshot = Guard.NotNullOrWhiteSpace(packagingNameSnapshot);
        ConversionToBaseSnapshot = Guard.Positive(conversionToBaseSnapshot);
        SuggestedUnitPrice = Guard.NonNegativeMoney(suggestedUnitPrice);
        ApplyPrice(priceOverride);
        ChangeQuantity(quantity);
        Status = OrderItemStatus.Pending;
    }

    public Guid OrderId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public StoreProduct StoreProduct { get; private set; } = null!;

    public Guid ProductPackagingId { get; private set; }

    public ProductPackaging ProductPackaging { get; private set; } = null!;

    public string ProductSkuSnapshot { get; private set; } = null!;

    public string ProductNameSnapshot { get; private set; } = null!;

    public string PackagingNameSnapshot { get; private set; } = null!;

    public long Quantity { get; private set; }

    public long ConversionToBaseSnapshot { get; private set; }

    public long BaseQuantity { get; private set; }

    // System suggested price from the applicable Price List.
    public decimal SuggestedUnitPrice { get; private set; }

    // Actual final selling price.
    public decimal UnitPrice { get; private set; }

    public decimal LineTotalAmount { get; private set; }

    public bool PriceOverridden { get; private set; }

    public string? OverrideReason { get; private set; }

    public Guid? OverriddenBy { get; private set; }

    public long FulfilledBaseQuantity { get; private set; }

    public long CancelledBaseQuantity { get; private set; }

    public OrderItemStatus Status { get; private set; }

    public long RemainingBaseQuantity => BaseQuantity - FulfilledBaseQuantity - CancelledBaseQuantity;

    internal void ChangeQuantity(long quantity)
    {
        Quantity = Guard.Positive(quantity);
        BaseQuantity = checked(Quantity * ConversionToBaseSnapshot);
        RecalculateLineTotal();
    }

    internal void ApplyPrice(PriceOverride? priceOverride)
    {
        if (priceOverride is null)
        {
            UnitPrice = SuggestedUnitPrice;
            PriceOverridden = false;
            OverrideReason = null;
            OverriddenBy = null;
        }
        else
        {
            UnitPrice = Guard.NonNegativeMoney(priceOverride.UnitPrice);
            OverrideReason = Guard.NotNullOrWhiteSpace(priceOverride.Reason);
            OverriddenBy = priceOverride.OverriddenBy == Guid.Empty
                ? throw new DomainException("A price override requires the overriding staff member.")
                : priceOverride.OverriddenBy;
            PriceOverridden = true;
        }

        RecalculateLineTotal();
    }

    internal void Fulfill(long baseQuantity)
    {
        Guard.Positive(baseQuantity);

        if (baseQuantity > RemainingBaseQuantity)
        {
            throw new DomainException(
                $"Cannot fulfill {baseQuantity}; only {RemainingBaseQuantity} base units remain on this order item.");
        }

        FulfilledBaseQuantity += baseQuantity;
        RefreshStatus();
    }

    internal void CancelRemaining()
    {
        if (RemainingBaseQuantity == 0)
        {
            throw new DomainException("Order item has no remaining quantity to cancel.");
        }

        CancelledBaseQuantity += RemainingBaseQuantity;
        RefreshStatus();
    }

    // Exact: quantity × a 2-decimal price needs no rounding.
    private void RecalculateLineTotal() => LineTotalAmount = Quantity * UnitPrice;

    private void RefreshStatus()
    {
        if (RemainingBaseQuantity > 0)
        {
            Status = FulfilledBaseQuantity > 0 ? OrderItemStatus.PartiallyFulfilled : OrderItemStatus.Pending;
        }
        else if (CancelledBaseQuantity == 0)
        {
            Status = OrderItemStatus.Fulfilled;
        }
        else
        {
            Status = FulfilledBaseQuantity == 0 ? OrderItemStatus.Cancelled : OrderItemStatus.PartiallyCancelled;
        }
    }
}
