using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Products.Entities;

// Created only through Product.AddPackaging. Conversion to base unit is fixed for the Product,
// so ConversionToBase and IsBaseUnit have no mutators; transactions snapshot the conversion.
public sealed class ProductPackaging : SoftDeletableEntity
{
    private ProductPackaging()
    {
    }

    internal ProductPackaging(
        Guid productId,
        Guid unitId,
        long conversionToBase,
        bool isBaseUnit,
        bool isPurchaseUnit,
        bool isSaleUnit,
        string status,
        string? packagingName,
        string? barcode)
    {
        Guard.Positive(conversionToBase);

        if (isBaseUnit && conversionToBase != 1)
        {
            throw new DomainException("Base packaging must have a conversion to base of 1.");
        }

        ProductId = productId;
        UnitId = unitId;
        ConversionToBase = conversionToBase;
        IsBaseUnit = isBaseUnit;
        UpdateDetails(packagingName, barcode, isPurchaseUnit, isSaleUnit);
        ChangeStatus(status);
    }

    public Guid ProductId { get; private set; }

    public Guid UnitId { get; private set; }

    public Unit Unit { get; private set; } = null!;

    public string? PackagingName { get; private set; }

    public long ConversionToBase { get; private set; }

    public bool IsBaseUnit { get; private set; }

    public bool IsPurchaseUnit { get; private set; }

    public bool IsSaleUnit { get; private set; }

    public string? Barcode { get; private set; }

    // Allowed values are not defined by the database design yet.
    public string Status { get; private set; } = null!;

    public void UpdateDetails(string? packagingName, string? barcode, bool isPurchaseUnit, bool isSaleUnit)
    {
        PackagingName = packagingName;
        Barcode = barcode;
        IsPurchaseUnit = isPurchaseUnit;
        IsSaleUnit = isSaleUnit;
    }

    public void ChangeStatus(string status) => Status = Guard.NotNullOrWhiteSpace(status);

    // A packaging used with a Store Product must belong to the Product behind that Store Product.
    public void EnsureBelongsTo(StoreProduct storeProduct)
    {
        if (ProductId != storeProduct.ProductId)
        {
            throw new DomainException("Product packaging does not belong to the store product's product.");
        }
    }
}
