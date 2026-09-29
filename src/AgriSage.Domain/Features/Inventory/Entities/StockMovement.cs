using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Domain.Features.Inventory.Entities;

// Header of an auditable physical inventory movement. Only POSTED movements affect inventory;
// POSTED movements and their items are immutable and are corrected by REVERSAL movements.
public sealed class StockMovement : SoftDeletableEntity
{
    private readonly List<StockMovementItem> _items = [];

    private StockMovement()
    {
    }

    public StockMovement(
        Guid storeId,
        string movementNumber,
        StockMovementType movementType,
        DateTimeOffset occurredAt,
        Guid createdBy,
        Guid? goodsReceiptId = null,
        Guid? orderId = null,
        Guid? deliveryId = null,
        Guid? stocktakeId = null,
        Guid? salesReturnId = null,
        Guid? reversalOfMovementId = null,
        string? reasonCode = null,
        string? reason = null)
    {
        if ((movementType == StockMovementType.Reversal) != (reversalOfMovementId is not null))
        {
            throw new DomainException("A REVERSAL movement, and only a REVERSAL movement, references the reversed movement.");
        }

        StoreId = storeId;
        MovementNumber = Guard.NotNullOrWhiteSpace(movementNumber);
        MovementType = movementType;
        Status = StockMovementStatus.Draft;
        OccurredAt = occurredAt;
        CreatedBy = createdBy;
        GoodsReceiptId = goodsReceiptId;
        OrderId = orderId;
        DeliveryId = deliveryId;
        StocktakeId = stocktakeId;
        SalesReturnId = salesReturnId;
        ReversalOfMovementId = reversalOfMovementId;
        ReasonCode = reasonCode;
        Reason = reason;
    }

    public Guid StoreId { get; private set; }

    public string MovementNumber { get; private set; } = null!;

    public StockMovementType MovementType { get; private set; }

    public StockMovementStatus Status { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? GoodsReceiptId { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? DeliveryId { get; private set; }

    public Guid? StocktakeId { get; private set; }

    // FK to sales_returns (entity introduced in a later task).
    public Guid? SalesReturnId { get; private set; }

    public Guid? ReversalOfMovementId { get; private set; }

    // Reason code values are examples only in the database design.
    public string? ReasonCode { get; private set; }

    public string? Reason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedBy { get; private set; }

    public IReadOnlyCollection<StockMovementItem> Items => _items.AsReadOnly();

    public StockMovementItem AddItem(Guid inventoryLotId, LotBalanceChange change, string? note = null)
    {
        EnsureStatus(StockMovementStatus.Draft);
        EnsureDirectionMatchesType(change.QuantityDelta);

        var item = new StockMovementItem(Id, inventoryLotId, change, note);
        _items.Add(item);

        return item;
    }

    public void Post(Guid postedBy, DateTimeOffset postedAt)
    {
        EnsureStatus(StockMovementStatus.Draft);

        if (!_items.Any(i => !i.IsDeleted))
        {
            throw new DomainException($"Stock movement '{MovementNumber}' has no items to post.");
        }

        Status = StockMovementStatus.Posted;
        PostedBy = postedBy;
        PostedAt = postedAt;
    }

    public void Cancel()
    {
        EnsureStatus(StockMovementStatus.Draft);
        Status = StockMovementStatus.Cancelled;
    }

    // Called when a REVERSAL movement for this movement is posted.
    public void MarkReversed()
    {
        EnsureStatus(StockMovementStatus.Posted);
        Status = StockMovementStatus.Reversed;
    }

    protected override void EnsureCanBeDeleted() => EnsureStatus(StockMovementStatus.Draft);

    private void EnsureDirectionMatchesType(long quantityDelta)
    {
        var valid = MovementType switch
        {
            StockMovementType.StockIn or StockMovementType.ReturnIn or StockMovementType.AdjustmentIn => quantityDelta > 0,
            StockMovementType.Sale or StockMovementType.AdjustmentOut => quantityDelta < 0,
            _ => quantityDelta != 0
        };

        if (!valid)
        {
            throw new DomainException($"Quantity delta {quantityDelta} does not match movement type {MovementType}.");
        }
    }

    private void EnsureStatus(StockMovementStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"Stock movement '{MovementNumber}' is {Status}; expected {expected}.");
        }
    }
}
