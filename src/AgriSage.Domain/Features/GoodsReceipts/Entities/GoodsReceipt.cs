using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Suppliers.Entities;

namespace AgriSage.Domain.Features.GoodsReceipts.Entities;

// Aggregate root for Goods Receipt Items. Only DRAFT receipts may be edited; CONFIRMED receipts are
// immutable and corrected through reversal/adjustment. Inventory posting is not part of this entity.
public sealed class GoodsReceipt : SoftDeletableEntity
{
    private readonly List<GoodsReceiptItem> _items = [];

    private GoodsReceipt()
    {
    }

    public GoodsReceipt(
        Guid storeId,
        Guid supplierId,
        string receiptNumber,
        DateTimeOffset receivedAt,
        Guid receivedBy,
        GoodsReceiptSourceType sourceType,
        string? sourceFileName = null,
        string? sourceFileUrl = null,
        string? supplierInvoiceNumber = null,
        DateOnly? supplierInvoiceDate = null,
        string? note = null)
    {
        StoreId = storeId;
        ReceiptNumber = Guard.NotNullOrWhiteSpace(receiptNumber);
        SourceType = sourceType;
        SourceFileName = sourceFileName;
        SourceFileUrl = sourceFileUrl;
        Status = GoodsReceiptStatus.Draft;
        UpdateHeader(supplierId, supplierInvoiceNumber, supplierInvoiceDate, receivedAt, receivedBy, note);
    }

    public Guid StoreId { get; private set; }

    public Guid SupplierId { get; private set; }

    public Supplier Supplier { get; private set; } = null!;

    public string ReceiptNumber { get; private set; } = null!;

    public string? SupplierInvoiceNumber { get; private set; }

    public DateOnly? SupplierInvoiceDate { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public GoodsReceiptSourceType SourceType { get; private set; }

    public string? SourceFileName { get; private set; }

    public string? SourceFileUrl { get; private set; }

    public GoodsReceiptStatus Status { get; private set; }

    public decimal SubtotalAmount { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<GoodsReceiptItem> Items => _items.AsReadOnly();

    public void UpdateHeader(
        Guid supplierId,
        string? supplierInvoiceNumber,
        DateOnly? supplierInvoiceDate,
        DateTimeOffset receivedAt,
        Guid receivedBy,
        string? note)
    {
        EnsureDraft();

        SupplierId = supplierId;
        SupplierInvoiceNumber = supplierInvoiceNumber;
        SupplierInvoiceDate = supplierInvoiceDate;
        ReceivedAt = receivedAt;
        ReceivedBy = receivedBy;
        Note = note;
    }

    // Conversion is snapshotted from the packaging itself, never taken from client input.
    // Lot/expiry requirements (Product flags) are validated by the receiving workflow.
    public GoodsReceiptItem AddItem(
        StoreProduct storeProduct,
        ProductPackaging productPackaging,
        long receivedQuantity,
        decimal purchaseUnitCost,
        string? supplierLotNumber = null,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null,
        string? note = null)
    {
        EnsureDraft();

        if (storeProduct.IsDeleted || productPackaging.IsDeleted)
        {
            throw new DomainException("Cannot receive a deleted store product or packaging.");
        }

        productPackaging.EnsureBelongsTo(storeProduct);

        var item = new GoodsReceiptItem(
            Id,
            storeProduct.Id,
            productPackaging.Id,
            productPackaging.ConversionToBase,
            receivedQuantity,
            purchaseUnitCost,
            supplierLotNumber,
            manufacturingDate,
            expiryDate,
            note);

        _items.Add(item);
        RecalculateTotals();

        return item;
    }

    public void UpdateItem(
        Guid itemId,
        long receivedQuantity,
        decimal purchaseUnitCost,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        string? note)
    {
        EnsureDraft();

        GetActiveItem(itemId)
            .Update(receivedQuantity, purchaseUnitCost, supplierLotNumber, manufacturingDate, expiryDate, note);

        RecalculateTotals();
    }

    public void RemoveItem(Guid itemId, Guid? deletedBy, DateTimeOffset deletedAt)
    {
        EnsureDraft();

        GetActiveItem(itemId).RemoveFromAggregate(deletedBy, deletedAt);

        RecalculateTotals();
    }

    public void Confirm(Guid confirmedBy, DateTimeOffset confirmedAt)
    {
        EnsureDraft();

        if (!_items.Any(i => !i.IsDeleted))
        {
            throw new DomainException($"Goods receipt '{ReceiptNumber}' has no items to confirm.");
        }

        Status = GoodsReceiptStatus.Confirmed;
        ConfirmedBy = confirmedBy;
        ConfirmedAt = confirmedAt;
    }

    // Only DRAFT receipts can be cancelled; CONFIRMED receipts are corrected via reversal/adjustment.
    public void Cancel(Guid cancelledBy, DateTimeOffset cancelledAt, string? reason = null)
    {
        EnsureDraft();

        Status = GoodsReceiptStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = reason;
    }

    protected override void EnsureCanBeDeleted() => EnsureDraft();

    private void EnsureDraft()
    {
        if (Status != GoodsReceiptStatus.Draft)
        {
            throw new DomainException($"Goods receipt '{ReceiptNumber}' is {Status}; only DRAFT receipts can be changed.");
        }
    }

    private GoodsReceiptItem GetActiveItem(Guid itemId) =>
        _items.SingleOrDefault(i => i.Id == itemId && !i.IsDeleted)
        ?? throw new DomainException($"Item '{itemId}' was not found on goods receipt '{ReceiptNumber}'.");

    // No tax/discount/fee columns exist, so total equals subtotal.
    private void RecalculateTotals()
    {
        SubtotalAmount = _items.Where(i => !i.IsDeleted).Sum(i => i.LineTotalAmount);
        TotalAmount = SubtotalAmount;
    }
}
