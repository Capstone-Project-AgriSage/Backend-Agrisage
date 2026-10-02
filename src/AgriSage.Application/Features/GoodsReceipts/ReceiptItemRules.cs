using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;

namespace AgriSage.Application.Features.GoodsReceipts;

// Rules for a goods receipt line (database design §23 and §35.17), checked when a line is saved and again when the
// receipt is confirmed, because the product, packaging and dates may have changed in between.
public static class ReceiptItemRules
{
    // Reasons why the line cannot be received, or null when it is acceptable.
    public static string? GetViolation(
        Product product,
        ProductPackaging packaging,
        string? supplierLotNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        DateOnly today)
    {
        if (product.Status == ProductStatus.Discontinued)
        {
            return "The product is DISCONTINUED and cannot be received.";
        }

        if (packaging.Status != PackagingStatus.Active)
        {
            return "The packaging is not ACTIVE.";
        }

        if (!packaging.IsPurchaseUnit)
        {
            return "The packaging is not a purchase unit.";
        }

        if (product.RequiresLotTracking && string.IsNullOrWhiteSpace(supplierLotNumber))
        {
            return "A lot number is required for this product.";
        }

        if (product.RequiresExpiryDate && expiryDate is null)
        {
            return "An expiry date is required for this product.";
        }

        if (expiryDate is not null && expiryDate < today)
        {
            return "The goods are already expired; expired stock cannot be received.";
        }

        if (manufacturingDate is not null && manufacturingDate > today)
        {
            return "The manufacturing date cannot be in the future.";
        }

        if (manufacturingDate is not null && expiryDate is not null && manufacturingDate > expiryDate)
        {
            return "The manufacturing date cannot be after the expiry date.";
        }

        return null;
    }
}
