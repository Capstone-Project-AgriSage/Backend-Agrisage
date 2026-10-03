using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;

namespace AgriSage.Application.Features.GoodsReceipts;

// The input of a receipt line a rule violation is about (the Excel import shows it as the column).
public enum ReceiptItemField
{
    Product,
    Packaging,
    LotNumber,
    ExpiryDate,
    ManufacturingDate
}

public sealed record ReceiptItemViolation(ReceiptItemField Field, string Message);

// Rules for a goods receipt line (database design §23 and §35.17), checked when a line is saved and again when the
// receipt is confirmed, because the product, packaging and dates may have changed in between. Manual entry and the
// Excel import (F4.3) use the same rules.
public static class ReceiptItemRules
{
    // Reasons why the line cannot be received, or null when it is acceptable.
    public static string? GetViolation(
        Product product,
        ProductPackaging packaging,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        DateOnly today) =>
        Check(product, packaging, supplierLotNumber, manufacturingDate, expiryDate, today)?.Message;

    // The first violation with the input it is about, or null when the line is acceptable.
    public static ReceiptItemViolation? Check(
        Product product,
        ProductPackaging packaging,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        DateOnly today)
    {
        if (product.Status == ProductStatus.Discontinued)
        {
            return new(ReceiptItemField.Product, "The product is DISCONTINUED and cannot be received.");
        }

        if (packaging.Status != PackagingStatus.Active)
        {
            return new(ReceiptItemField.Packaging, "The packaging is not ACTIVE.");
        }

        if (!packaging.IsPurchaseUnit)
        {
            return new(ReceiptItemField.Packaging, "The packaging is not a purchase unit.");
        }

        if (product.RequiresLotTracking && string.IsNullOrWhiteSpace(supplierLotNumber))
        {
            return new(ReceiptItemField.LotNumber, "A lot number is required for this product.");
        }

        if (product.RequiresExpiryDate && expiryDate is null)
        {
            return new(ReceiptItemField.ExpiryDate, "An expiry date is required for this product.");
        }

        if (expiryDate is not null && expiryDate < today)
        {
            return new(ReceiptItemField.ExpiryDate, "The goods are already expired; expired stock cannot be received.");
        }

        if (manufacturingDate is not null && manufacturingDate > today)
        {
            return new(ReceiptItemField.ManufacturingDate, "The manufacturing date cannot be in the future.");
        }

        if (manufacturingDate is not null && expiryDate is not null && manufacturingDate > expiryDate)
        {
            return new(ReceiptItemField.ManufacturingDate, "The manufacturing date cannot be after the expiry date.");
        }

        return null;
    }
}
