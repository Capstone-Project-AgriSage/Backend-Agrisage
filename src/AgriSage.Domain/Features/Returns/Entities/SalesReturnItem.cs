using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Returns.Enums;

namespace AgriSage.Domain.Features.Returns.Entities;

// One returned quantity traced to the exact sold Lot. Created, inspected and linked only through SalesReturn.
public sealed class SalesReturnItem : SoftDeletableChildEntity
{
    private SalesReturnItem()
    {
    }

    internal SalesReturnItem(
        Guid salesReturnId,
        Guid orderItemId,
        Guid inventoryLotId,
        long returnedBaseQuantity,
        decimal sellingUnitPriceSnapshot,
        long conversionToBaseSnapshot,
        string reasonCode,
        Guid? deliveryItemId,
        Guid? deliveryItemLotAllocationId,
        Guid? originalStockMovementItemId,
        decimal? originalCogsUnitCost)
    {
        // Exactly one fulfillment source (database design §35.10 / §35.14).
        if ((deliveryItemLotAllocationId is null) == (originalStockMovementItemId is null))
        {
            throw new DomainException(
                "A return line references exactly one source: its delivery lot allocation or its original sale stock movement item.");
        }

        SalesReturnId = salesReturnId;
        OrderItemId = orderItemId;
        InventoryLotId = inventoryLotId;
        DeliveryItemId = deliveryItemId;
        DeliveryItemLotAllocationId = deliveryItemLotAllocationId;
        OriginalStockMovementItemId = originalStockMovementItemId;
        ReturnedBaseQuantity = Guard.Positive(returnedBaseQuantity);
        SellingUnitPriceSnapshot = Guard.NonNegativeMoney(sellingUnitPriceSnapshot);
        ConversionToBaseSnapshot = Guard.Positive(conversionToBaseSnapshot);
        ReasonCode = Guard.NotNullOrWhiteSpace(reasonCode);

        // Original actual selling price per base unit, multiplied before dividing (database design §35.10).
        ReturnValue = CostRounding.RoundMoney(
            ReturnedBaseQuantity * SellingUnitPriceSnapshot / ConversionToBaseSnapshot);

        if (originalCogsUnitCost is not null)
        {
            OriginalCogsUnitCost = Guard.UnitCost(originalCogsUnitCost.Value);
            ReturnInventoryCostValue = ReturnedBaseQuantity * OriginalCogsUnitCost;
        }

        ConditionStatus = ReturnConditionStatus.PendingInspection;
        InventoryDisposition = InventoryDisposition.None;
    }

    public Guid SalesReturnId { get; private set; }

    public Guid OrderItemId { get; private set; }

    public Guid? DeliveryItemId { get; private set; }

    public Guid? DeliveryItemLotAllocationId { get; private set; }

    public Guid? OriginalStockMovementItemId { get; private set; }

    public Guid InventoryLotId { get; private set; }

    public long ReturnedBaseQuantity { get; private set; }

    public decimal SellingUnitPriceSnapshot { get; private set; }

    public long ConversionToBaseSnapshot { get; private set; }

    public decimal ReturnValue { get; private set; }

    public decimal? OriginalCogsUnitCost { get; private set; }

    public decimal? ReturnInventoryCostValue { get; private set; }

    // Suggested values only in the database design (WRONG_PRODUCT, DAMAGED_PRODUCT, ...).
    public string ReasonCode { get; private set; } = null!;

    public ReturnConditionStatus ConditionStatus { get; private set; }

    public InventoryDisposition InventoryDisposition { get; private set; }

    public string? InspectionNote { get; private set; }

    public Guid? ReturnStockMovementId { get; private set; }

    public Guid? DebtAdjustmentTransactionId { get; private set; }

    public bool IsInspected => ConditionStatus != ReturnConditionStatus.PendingInspection;

    // Disposition is fixed by the condition (database design §35.11).
    internal void Inspect(ReturnConditionStatus conditionStatus, string? inspectionNote)
    {
        InventoryDisposition = conditionStatus switch
        {
            ReturnConditionStatus.Resellable => InventoryDisposition.Restock,
            ReturnConditionStatus.Damaged or ReturnConditionStatus.Expired or ReturnConditionStatus.Unusable =>
                InventoryDisposition.WriteOff,
            _ => throw new DomainException("Inspection must set a condition other than PENDING_INSPECTION.")
        };

        ConditionStatus = conditionStatus;
        InspectionNote = inspectionNote;
    }

    internal void LinkReturnStockMovement(Guid stockMovementId)
    {
        if (InventoryDisposition != InventoryDisposition.Restock)
        {
            throw new DomainException("Only a RESTOCK return line re-enters inventory through RETURN_IN.");
        }

        if (ReturnStockMovementId is not null)
        {
            throw new DomainException("This return line is already linked to its RETURN_IN stock movement.");
        }

        ReturnStockMovementId = stockMovementId;
    }

    internal void LinkDebtAdjustmentTransaction(Guid debtTransactionId)
    {
        if (DebtAdjustmentTransactionId is not null)
        {
            throw new DomainException("This return line is already linked to a debt adjustment transaction.");
        }

        DebtAdjustmentTransactionId = debtTransactionId;
    }
}
