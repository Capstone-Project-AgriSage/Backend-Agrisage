using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Domain.Features.Returns.Entities;

// Post-fulfillment return case; aggregate root for its lines and Refunds (database design §35.9–35.11).
// A delivery refusal before fulfillment is a Delivery Incident, not a Sales Return. The original Order is
// never rewritten. RETURN_IN stock movements, RETURN debt transactions, "already returned" totals and
// refund execution are done by the calling use cases; this aggregate only records and checks them.
public sealed class SalesReturn : SoftDeletableEntity
{
    private readonly List<SalesReturnItem> _items = [];
    private readonly List<Refund> _refunds = [];

    private SalesReturn()
    {
    }

    public SalesReturn(
        Order order,
        string returnNumber,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        string? reasonSummary = null,
        string? note = null)
    {
        StoreId = order.StoreId;
        OrderId = order.Id;
        FarmerProfileId = order.FarmerProfileId; // Registered: the original Order's Farmer; walk-in: null.
        ReturnNumber = Guard.NotNullOrWhiteSpace(returnNumber);
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        ReasonSummary = reasonSummary;
        Note = note;
        Status = SalesReturnStatus.Requested;
    }

    public Guid StoreId { get; private set; }

    public string ReturnNumber { get; private set; } = null!;

    public Guid OrderId { get; private set; }

    public Guid? FarmerProfileId { get; private set; }

    public FarmerProfile? FarmerProfile { get; private set; }

    public SalesReturnStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid RequestedBy { get; private set; }

    public string? ReasonSummary { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public Guid? ReceivedBy { get; private set; }

    public DateTimeOffset? InspectedAt { get; private set; }

    public Guid? InspectedBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public decimal TotalReturnAmount { get; private set; }

    public decimal TotalRefundAmount { get; private set; }

    public decimal TotalDebtAdjustment { get; private set; }

    public string? Note { get; private set; }

    public IReadOnlyCollection<SalesReturnItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    // alreadyReturnedBaseQuantity: this Order Item's quantity on all other returns that are not REJECTED or
    // CANCELLED, calculated by the Application. Price and conversion are snapshotted from the Order Item.
    // The source must match the Order's fulfillment type: DELIVERY → lot allocation, PICKUP → original
    // sale stock movement item.
    public SalesReturnItem AddItem(
        Order order,
        OrderItem orderItem,
        long returnedBaseQuantity,
        long alreadyReturnedBaseQuantity,
        Guid inventoryLotId,
        string reasonCode,
        Guid? deliveryItemId = null,
        Guid? deliveryItemLotAllocationId = null,
        Guid? originalStockMovementItemId = null,
        decimal? originalCogsUnitCost = null)
    {
        EnsureStatus(SalesReturnStatus.Requested);

        if (order.Id != OrderId)
        {
            throw new DomainException("The order is not this return's order.");
        }

        if (orderItem.OrderId != OrderId || orderItem.IsDeleted)
        {
            throw new DomainException("The order item does not belong to this return's order.");
        }

        var sourceMatches = order.FulfillmentType == FulfillmentType.Delivery
            ? deliveryItemLotAllocationId is not null && originalStockMovementItemId is null
            : originalStockMovementItemId is not null && deliveryItemLotAllocationId is null;

        if (!sourceMatches)
        {
            throw new DomainException(order.FulfillmentType == FulfillmentType.Delivery
                ? "A DELIVERY order's return line references its delivery lot allocation only."
                : "A PICKUP order's return line references its original sale stock movement item only.");
        }

        Guard.NotNegative(alreadyReturnedBaseQuantity);

        var onThisReturn = ActiveItems.Where(i => i.OrderItemId == orderItem.Id).Sum(i => i.ReturnedBaseQuantity);
        var returnable = orderItem.FulfilledBaseQuantity - alreadyReturnedBaseQuantity - onThisReturn;

        if (Guard.Positive(returnedBaseQuantity) > returnable)
        {
            throw new DomainException(
                $"Cannot return {returnedBaseQuantity} base units; only {Math.Max(returnable, 0)} fulfilled units remain returnable.");
        }

        var item = new SalesReturnItem(
            Id,
            orderItem.Id,
            inventoryLotId,
            returnedBaseQuantity,
            orderItem.UnitPrice,
            orderItem.ConversionToBaseSnapshot,
            reasonCode,
            deliveryItemId,
            deliveryItemLotAllocationId,
            originalStockMovementItemId,
            originalCogsUnitCost);

        _items.Add(item);
        RecalculateReturnAmount();

        return item;
    }

    public void RemoveItem(Guid itemId, Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureStatus(SalesReturnStatus.Requested);
        GetItem(itemId).RemoveFromAggregate(deletedBy, deletedAt);
        RecalculateReturnAmount();
    }

    public void Approve(Guid approvedBy, DateTimeOffset approvedAt)
    {
        EnsureStatus(SalesReturnStatus.Requested);

        if (!ActiveItems.Any())
        {
            throw new DomainException($"Sales return '{ReturnNumber}' has no lines to approve.");
        }

        Status = SalesReturnStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
    }

    // Status only (no rejected_* columns); actor and reason are recorded through audit logging.
    public void Reject()
    {
        EnsureStatus(SalesReturnStatus.Requested);
        Status = SalesReturnStatus.Rejected;
    }

    public void Cancel(Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null)
    {
        EnsureStatus(SalesReturnStatus.Requested, SalesReturnStatus.Approved);
        Status = SalesReturnStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = reason;
    }

    public void MarkReceived(Guid receivedBy, DateTimeOffset receivedAt)
    {
        EnsureStatus(SalesReturnStatus.Approved);
        Status = SalesReturnStatus.Received;
        ReceivedBy = receivedBy;
        ReceivedAt = receivedAt;
    }

    public void InspectItem(Guid itemId, ReturnConditionStatus conditionStatus, string? inspectionNote = null)
    {
        EnsureStatus(SalesReturnStatus.Received);
        GetItem(itemId).Inspect(conditionStatus, inspectionNote);
    }

    // Records the settlement once: unpaid attributable debt is reduced first (amount calculated by the
    // Application), the already-paid remainder is refunded, so each returned value is compensated exactly once.
    public void CompleteInspection(Guid inspectedBy, DateTimeOffset inspectedAt, decimal debtAdjustmentAmount)
    {
        EnsureStatus(SalesReturnStatus.Received);

        if (ActiveItems.Any(i => !i.IsInspected))
        {
            throw new DomainException($"Sales return '{ReturnNumber}' has lines that are not inspected yet.");
        }

        if (Guard.NonNegativeMoney(debtAdjustmentAmount) > TotalReturnAmount)
        {
            throw new DomainException(
                $"Debt adjustment {debtAdjustmentAmount} exceeds the total return value {TotalReturnAmount}.");
        }

        TotalDebtAdjustment = debtAdjustmentAmount;
        TotalRefundAmount = TotalReturnAmount - debtAdjustmentAmount;
        Status = SalesReturnStatus.Inspected;
        InspectedBy = inspectedBy;
        InspectedAt = inspectedAt;
    }

    public void LinkReturnStockMovement(Guid itemId, Guid stockMovementId)
    {
        EnsureResolving();
        GetItem(itemId).LinkReturnStockMovement(stockMovementId);
        MarkResolutionStep();
    }

    public void LinkDebtAdjustmentTransaction(Guid itemId, Guid debtTransactionId)
    {
        EnsureResolving();

        if (TotalDebtAdjustment == 0)
        {
            throw new DomainException($"Sales return '{ReturnNumber}' has no debt adjustment.");
        }

        GetItem(itemId).LinkDebtAdjustmentTransaction(debtTransactionId);
        MarkResolutionStep();
    }

    public Refund RequestRefund(
        string refundNumber,
        RefundMethod refundMethod,
        decimal amount,
        Guid requestedBy,
        DateTimeOffset requestedAt,
        Guid? originalPaymentId = null,
        string? note = null,
        string? externalReference = null)
    {
        EnsureResolving();

        var committed = ActiveRefunds.Where(r => r.CountsTowardRefundTotal).Sum(r => r.Amount);

        if (committed + Guard.PositiveMoney(amount) > TotalRefundAmount)
        {
            throw new DomainException(
                $"Refund {amount} exceeds the remaining refundable amount {TotalRefundAmount - committed}.");
        }

        var refund = new Refund(Id, StoreId, refundNumber, refundMethod, amount, requestedBy, requestedAt, originalPaymentId, note);
        refund.SetPendingDetails(externalReference, null);
        _refunds.Add(refund);

        return refund;
    }

    public void CompleteRefund(
        Guid refundId,
        Guid completedBy,
        DateTimeOffset completedAt,
        string? externalReference = null,
        string? proofFileUrl = null,
        string? note = null)
    {
        EnsureResolving();
        var refund = GetRefund(refundId);
        refund.SetPendingDetails(null, note);
        refund.Complete(completedBy, completedAt, externalReference, proofFileUrl);
        MarkResolutionStep();
    }

    public void FailRefund(Guid refundId, string? note = null)
    {
        EnsureResolving();
        var refund = GetRefund(refundId);
        refund.SetPendingDetails(null, note);
        refund.Fail();
    }

    public void CancelRefund(Guid refundId, Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null)
    {
        EnsureResolving();
        GetRefund(refundId).Cancel(cancelledBy, cancelledAt, reason);
    }

    // COMPLETED: every RESTOCK line has its RETURN_IN and completed refunds equal the refund total.
    public void Complete(DateTimeOffset completedAt)
    {
        EnsureResolving();

        if (ActiveItems.Any(i => i.InventoryDisposition == InventoryDisposition.Restock && i.ReturnStockMovementId is null))
        {
            throw new DomainException($"Sales return '{ReturnNumber}' has RESTOCK lines without a RETURN_IN stock movement.");
        }

        var refunded = ActiveRefunds.Where(r => r.Status == RefundStatus.Completed).Sum(r => r.Amount);

        if (refunded != TotalRefundAmount)
        {
            throw new DomainException(
                $"Completed refunds {refunded} do not equal the refund total {TotalRefundAmount}.");
        }

        Status = SalesReturnStatus.Completed;
        CompletedAt = completedAt;
    }

    protected override void EnsureCanBeDeleted() => EnsureStatus(SalesReturnStatus.Requested);

    // Called once the inspection's stock/debt steps are linked. Remaining money is handled by the refund flow.
    public void FinishInspectionResolution(DateTimeOffset at)
    {
        EnsureResolving();
        if (ActiveItems.Any(i => i.InventoryDisposition == InventoryDisposition.Restock && i.ReturnStockMovementId is null))
        {
            throw new DomainException("Every RESTOCK line must be linked before finishing inspection resolution.");
        }
        if (TotalRefundAmount == 0)
            Complete(at);
        else
            Status = SalesReturnStatus.PartiallyResolved;
    }

    private IEnumerable<SalesReturnItem> ActiveItems => _items.Where(i => !i.IsDeleted);

    private IEnumerable<Refund> ActiveRefunds => _refunds.Where(r => !r.IsDeleted);

    private SalesReturnItem GetItem(Guid itemId) =>
        ActiveItems.SingleOrDefault(i => i.Id == itemId)
        ?? throw new DomainException($"Line '{itemId}' was not found on sales return '{ReturnNumber}'.");

    private Refund GetRefund(Guid refundId) =>
        ActiveRefunds.SingleOrDefault(r => r.Id == refundId)
        ?? throw new DomainException($"Refund '{refundId}' was not found on sales return '{ReturnNumber}'.");

    private void RecalculateReturnAmount() => TotalReturnAmount = ActiveItems.Sum(i => i.ReturnValue);

    private void MarkResolutionStep()
    {
        if (Status == SalesReturnStatus.Inspected)
        {
            Status = SalesReturnStatus.PartiallyResolved;
        }
    }

    private void EnsureResolving() => EnsureStatus(SalesReturnStatus.Inspected, SalesReturnStatus.PartiallyResolved);

    private void EnsureStatus(params SalesReturnStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException($"Sales return '{ReturnNumber}' is {Status}; this action is not allowed.");
        }
    }
}
