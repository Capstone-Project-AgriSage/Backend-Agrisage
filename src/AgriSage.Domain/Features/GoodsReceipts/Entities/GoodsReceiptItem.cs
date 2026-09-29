using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.GoodsReceipts.Entities;

// Created, changed and removed only through GoodsReceipt, which enforces DRAFT-only editing.
public sealed class GoodsReceiptItem : SoftDeletableChildEntity
{
    private GoodsReceiptItem()
    {
    }

    internal GoodsReceiptItem(
        Guid goodsReceiptId,
        Guid storeProductId,
        Guid productPackagingId,
        long conversionToBaseSnapshot,
        long receivedQuantity,
        decimal purchaseUnitCost,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        string? note)
    {
        GoodsReceiptId = goodsReceiptId;
        StoreProductId = storeProductId;
        ProductPackagingId = productPackagingId;
        ConversionToBaseSnapshot = Guard.Positive(conversionToBaseSnapshot);
        Update(receivedQuantity, purchaseUnitCost, supplierLotNumber, manufacturingDate, expiryDate, note);
    }

    public Guid GoodsReceiptId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public StoreProduct StoreProduct { get; private set; } = null!;

    public Guid ProductPackagingId { get; private set; }

    public ProductPackaging ProductPackaging { get; private set; } = null!;

    public long ReceivedQuantity { get; private set; }

    public long ConversionToBaseSnapshot { get; private set; }

    public long BaseQuantity { get; private set; }

    public decimal PurchaseUnitCost { get; private set; }

    public decimal BaseUnitCost { get; private set; }

    public decimal LineTotalAmount { get; private set; }

    public string? SupplierLotNumber { get; private set; }

    public DateOnly? ManufacturingDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    // NULL while DRAFT; resolved by the confirmation workflow (later task).
    public Guid? InventoryLotId { get; private set; }

    public string? Note { get; private set; }

    internal void Update(
        long receivedQuantity,
        decimal purchaseUnitCost,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        string? note)
    {
        ReceivedQuantity = Guard.Positive(receivedQuantity);
        PurchaseUnitCost = Guard.NonNegativeMoney(purchaseUnitCost);

        BaseQuantity = checked(ReceivedQuantity * ConversionToBaseSnapshot);
        BaseUnitCost = CostRounding.RoundUnitCost(PurchaseUnitCost / ConversionToBaseSnapshot);

        // Exact: quantity × a 2-decimal cost needs no rounding.
        LineTotalAmount = ReceivedQuantity * PurchaseUnitCost;

        SupplierLotNumber = supplierLotNumber;
        ManufacturingDate = manufacturingDate;
        ExpiryDate = expiryDate;
        Note = note;
    }
}
