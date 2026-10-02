using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Domain.Features.Orders.Entities;

// Online Farmer order or Store counter order; aggregate root for Order Items and for the Refunds of order
// prepayment left unused when the Order is cancelled in full or in part (database design §35.18).
// Lifecycle (database design §35.4): PENDING_CONFIRMATION → CONFIRMED → [PREPARING] → READY_FOR_FULFILLMENT,
// then status is derived from fulfilled/cancelled quantities. Items change only while PENDING_CONFIRMATION.
// Credit profile checks, reservations, payments and debt posting are done by the calling use cases.
public sealed class Order : SoftDeletableEntity, IHasConcurrencyVersion
{
    private readonly List<OrderItem> _items = [];
    private readonly List<Refund> _cancellationRefunds = [];

    private Order()
    {
    }

    public Order(
        Guid storeId,
        string orderNumber,
        OrderSource source,
        CustomerType customerType,
        Guid createdBy,
        string customerNameSnapshot,
        SettlementType settlementType,
        FulfillmentType fulfillmentType,
        Guid? farmerProfileId = null,
        string? customerPhoneSnapshot = null,
        Guid? customerGroupIdSnapshot = null,
        Guid? priceListIdSnapshot = null,
        DeliveryAddress? deliveryAddress = null,
        Guid? sourceAddressId = null,
        string? note = null)
    {
        EnsureCustomerAndSettlement(customerType, farmerProfileId, settlementType);

        StoreId = storeId;
        OrderNumber = Guard.NotNullOrWhiteSpace(orderNumber);
        Source = source;
        CustomerType = customerType;
        FarmerProfileId = farmerProfileId;
        CreatedBy = createdBy;
        CustomerNameSnapshot = Guard.NotNullOrWhiteSpace(customerNameSnapshot);
        CustomerPhoneSnapshot = customerPhoneSnapshot;
        CustomerGroupIdSnapshot = customerGroupIdSnapshot;
        PriceListIdSnapshot = priceListIdSnapshot;
        SettlementType = settlementType;
        FulfillmentType = fulfillmentType;
        SourceAddressId = sourceAddressId;
        Note = note;
        Status = OrderStatus.PendingConfirmation;
        SetDeliveryAddress(deliveryAddress);
    }

    public Guid StoreId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public OrderSource Source { get; private set; }

    public CustomerType CustomerType { get; private set; }

    public Guid? FarmerProfileId { get; private set; }

    public FarmerProfile? FarmerProfile { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? CustomerGroupIdSnapshot { get; private set; }

    public Guid? PriceListIdSnapshot { get; private set; }

    public string CustomerNameSnapshot { get; private set; } = null!;

    public string? CustomerPhoneSnapshot { get; private set; }

    public SettlementType SettlementType { get; private set; }

    // Captured at confirmation of a CREDIT order; later Credit Tier changes do not alter it.
    public int? CreditTermDaysSnapshot { get; private set; }

    public FulfillmentType FulfillmentType { get; private set; }

    public Guid? SourceAddressId { get; private set; }

    public string? RecipientNameSnapshot { get; private set; }

    public string? RecipientPhoneSnapshot { get; private set; }

    public string? DeliveryAddressLine { get; private set; }

    public string? DeliveryWard { get; private set; }

    public string? DeliveryDistrict { get; private set; }

    public string? DeliveryProvince { get; private set; }

    public decimal? DeliveryLatitude { get; private set; }

    public decimal? DeliveryLongitude { get; private set; }

    public OrderStatus Status { get; private set; }

    public decimal SubtotalAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? PickupCompletedAt { get; private set; }

    public Guid? PickupCompletedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<Refund> CancellationRefunds => _cancellationRefunds.AsReadOnly();

    public OrderItem AddItem(
        StoreProduct storeProduct,
        ProductPackaging productPackaging,
        string productSkuSnapshot,
        string productNameSnapshot,
        string packagingNameSnapshot,
        long quantity,
        decimal suggestedUnitPrice,
        PriceOverride? priceOverride = null)
    {
        EnsurePending();

        if (storeProduct.IsDeleted || productPackaging.IsDeleted)
        {
            throw new DomainException("Cannot order a deleted store product or packaging.");
        }

        productPackaging.EnsureBelongsTo(storeProduct);

        var item = new OrderItem(
            Id,
            storeProduct.Id,
            productPackaging.Id,
            productSkuSnapshot,
            productNameSnapshot,
            packagingNameSnapshot,
            quantity,
            productPackaging.ConversionToBase,
            suggestedUnitPrice,
            priceOverride);

        _items.Add(item);
        RecalculateTotals();

        return item;
    }

    public void UpdateItemQuantity(Guid itemId, long quantity)
    {
        EnsurePending();
        GetItem(itemId).ChangeQuantity(quantity);
        RecalculateTotals();
    }

    // Null restores the suggested price.
    public void OverrideItemPrice(Guid itemId, PriceOverride? priceOverride)
    {
        EnsurePending();
        GetItem(itemId).ApplyPrice(priceOverride);
        RecalculateTotals();
    }

    public void RemoveItem(Guid itemId, Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsurePending();
        GetItem(itemId).RemoveFromAggregate(deletedBy, deletedAt);
        RecalculateTotals();
    }

    public void UpdateDeliveryAddress(DeliveryAddress? deliveryAddress, Guid? sourceAddressId)
    {
        EnsurePending();
        SetDeliveryAddress(deliveryAddress);
        SourceAddressId = sourceAddressId;
    }

    public void Confirm(Guid confirmedBy, DateTimeOffset confirmedAt, int? creditTermDays = null)
    {
        EnsurePending();

        if (!ActiveItems.Any())
        {
            throw new DomainException($"Order '{OrderNumber}' has no items to confirm.");
        }

        if (SettlementType == SettlementType.Credit)
        {
            if (creditTermDays is null or < 0)
            {
                throw new DomainException("A CREDIT order requires a non-negative credit term (days) at confirmation.");
            }
        }
        else if (creditTermDays is not null)
        {
            throw new DomainException("Only CREDIT orders carry a credit term.");
        }

        CreditTermDaysSnapshot = creditTermDays;
        Status = OrderStatus.Confirmed;
        ConfirmedBy = confirmedBy;
        ConfirmedAt = confirmedAt;
    }

    public void StartPreparing()
    {
        EnsureStatus(OrderStatus.Confirmed);
        Status = OrderStatus.Preparing;
    }

    public void MarkReadyForFulfillment()
    {
        EnsureStatus(OrderStatus.Confirmed, OrderStatus.Preparing);
        Status = OrderStatus.ReadyForFulfillment;
    }

    // Records base quantity actually handed over (pickup) or delivered.
    public void RecordFulfillment(Guid itemId, long baseQuantity, Guid performedBy, DateTimeOffset performedAt)
    {
        EnsureFulfillable();
        GetItem(itemId).Fulfill(baseQuantity);
        RefreshFulfillmentStatus(performedBy, performedAt);
    }

    public void CancelItemRemaining(Guid itemId, Guid cancelledBy, DateTimeOffset cancelledAt)
    {
        EnsureFulfillable();
        GetItem(itemId).CancelRemaining();
        RefreshFulfillmentStatus(cancelledBy, cancelledAt);
    }

    // Full cancellation is only possible before anything has been fulfilled.
    public void Cancel(Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null)
    {
        EnsureStatus(
            OrderStatus.PendingConfirmation,
            OrderStatus.Confirmed,
            OrderStatus.Preparing,
            OrderStatus.ReadyForFulfillment);

        foreach (var item in ActiveItems.Where(i => i.RemainingBaseQuantity > 0))
        {
            item.CancelRemaining();
        }

        Status = OrderStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = reason;
    }

    // Money paid for this Order that will never be consumed because the Order (or its remainder) was cancelled.
    // The caller has reversed the payment's unconsumed ORDER allocation and checks that the amount does not exceed
    // it (cross-aggregate); staff hand the money back outside the system (no automatic payOS refund).
    public Refund RequestCancellationRefund(
        string refundNumber,
        Guid originalPaymentId,
        RefundMethod refundMethod,
        decimal amount,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        string? note = null)
    {
        EnsureStatus(OrderStatus.Cancelled, OrderStatus.PartiallyCancelled);

        var refund = Refund.ForCancelledOrder(
            StoreId, refundNumber, Id, originalPaymentId, refundMethod, amount, requestedBy, requestedAt, note);
        _cancellationRefunds.Add(refund);

        return refund;
    }

    public void CompleteCancellationRefund(
        Guid refundId,
        Guid completedBy,
        DateTimeOffset completedAt,
        string? externalReference = null,
        string? proofFileUrl = null) =>
        GetCancellationRefund(refundId).Complete(completedBy, completedAt, externalReference, proofFileUrl);

    // A failed refund is retried with a new one.
    public void FailCancellationRefund(Guid refundId) => GetCancellationRefund(refundId).Fail();

    public void CancelCancellationRefund(Guid refundId, Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null) =>
        GetCancellationRefund(refundId).Cancel(cancelledBy, cancelledAt, reason);

    protected override void EnsureCanBeDeleted() => EnsurePending();

    private IEnumerable<OrderItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private static void EnsureCustomerAndSettlement(
        CustomerType customerType,
        Guid? farmerProfileId,
        SettlementType settlementType)
    {
        if (customerType == CustomerType.Registered && farmerProfileId is null)
        {
            throw new DomainException("A REGISTERED order requires a Farmer Profile.");
        }

        if (customerType == CustomerType.WalkIn)
        {
            if (farmerProfileId is not null)
            {
                throw new DomainException("A WALK_IN order cannot reference a Farmer Profile.");
            }

            if (settlementType != SettlementType.FullPayment)
            {
                throw new DomainException("A WALK_IN order can only use FULL_PAYMENT settlement.");
            }
        }
    }

    private void SetDeliveryAddress(DeliveryAddress? deliveryAddress)
    {
        if (FulfillmentType == FulfillmentType.Delivery)
        {
            if (deliveryAddress is null)
            {
                throw new DomainException("A DELIVERY order requires a delivery address.");
            }

            deliveryAddress.Validate();
        }

        RecipientNameSnapshot = deliveryAddress?.RecipientName;
        RecipientPhoneSnapshot = deliveryAddress?.RecipientPhone;
        DeliveryAddressLine = deliveryAddress?.AddressLine;
        DeliveryWard = deliveryAddress?.Ward;
        DeliveryDistrict = deliveryAddress?.District;
        DeliveryProvince = deliveryAddress?.Province;
        DeliveryLatitude = deliveryAddress?.Latitude;
        DeliveryLongitude = deliveryAddress?.Longitude;
    }

    private Refund GetCancellationRefund(Guid refundId) =>
        _cancellationRefunds.SingleOrDefault(r => r.Id == refundId && !r.IsDeleted)
        ?? throw new DomainException($"Refund '{refundId}' was not found on order '{OrderNumber}'.");

    private OrderItem GetItem(Guid itemId) =>
        ActiveItems.SingleOrDefault(i => i.Id == itemId)
        ?? throw new DomainException($"Item '{itemId}' was not found on order '{OrderNumber}'.");

    private void RecalculateTotals()
    {
        SubtotalAmount = ActiveItems.Sum(i => i.LineTotalAmount);
        TotalAmount = SubtotalAmount;
    }

    private void RefreshFulfillmentStatus(Guid actorId, DateTimeOffset at)
    {
        var items = ActiveItems.ToList();
        var fulfilled = items.Sum(i => i.FulfilledBaseQuantity);
        var cancelled = items.Sum(i => i.CancelledBaseQuantity);
        var remaining = items.Sum(i => i.RemainingBaseQuantity);

        if (remaining > 0)
        {
            if (fulfilled > 0)
            {
                Status = OrderStatus.PartiallyFulfilled;
            }

            return;
        }

        if (fulfilled == 0)
        {
            Status = OrderStatus.Cancelled;
            CancelledBy = actorId;
            CancelledAt = at;
            return;
        }

        Status = cancelled == 0 ? OrderStatus.Completed : OrderStatus.PartiallyCancelled;
        CompletedAt = at;

        if (FulfillmentType == FulfillmentType.Pickup)
        {
            PickupCompletedBy = actorId;
            PickupCompletedAt = at;
        }
    }

    private void EnsurePending() => EnsureStatus(OrderStatus.PendingConfirmation);

    private void EnsureFulfillable() =>
        EnsureStatus(
            OrderStatus.Confirmed,
            OrderStatus.Preparing,
            OrderStatus.ReadyForFulfillment,
            OrderStatus.PartiallyFulfilled);

    private void EnsureStatus(params OrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException($"Order '{OrderNumber}' is {Status}; this action is not allowed.");
        }
    }
}
