using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Products.Enums;

namespace AgriSage.Domain.Features.Products.Entities;

// Aggregate root for its Product Packagings. Packaging invariants are checked against the loaded
// Packagings collection; partial unique indexes are the database backstop.
public sealed class Product : SoftDeletableEntity
{
    private readonly List<ProductPackaging> _packagings = [];

    private Product()
    {
    }

    public Product(
        Guid categoryId,
        string sku,
        string name,
        bool requiresLotTracking = true,
        bool requiresExpiryDate = true,
        Guid? brandId = null,
        string? description = null,
        string? usageInstructions = null,
        string? imageUrl = null,
        ProductStatus status = ProductStatus.Active)
    {
        // Expiry is tracked per Inventory Lot: a product without lot tracking has one no-lot bucket that cannot
        // keep different expiry dates apart (database design §35.17).
        if (requiresExpiryDate && !requiresLotTracking)
        {
            throw new DomainException("A product that requires an expiry date must also require lot tracking.");
        }

        Sku = Guard.NotNullOrWhiteSpace(sku);
        RequiresLotTracking = requiresLotTracking;
        RequiresExpiryDate = requiresExpiryDate;
        UpdateDetails(categoryId, brandId, name, description, usageInstructions, imageUrl);
        Status = status;
    }

    public Guid CategoryId { get; private set; }

    public Category Category { get; private set; } = null!;

    public Guid? BrandId { get; private set; }

    public Brand? Brand { get; private set; }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? UsageInstructions { get; private set; }

    public bool RequiresLotTracking { get; private set; }

    public bool RequiresExpiryDate { get; private set; }

    public string? ImageUrl { get; private set; }

    public ProductStatus Status { get; private set; }

    public IReadOnlyCollection<ProductPackaging> Packagings => _packagings.AsReadOnly();

    public void UpdateDetails(
        Guid categoryId,
        Guid? brandId,
        string name,
        string? description,
        string? usageInstructions,
        string? imageUrl)
    {
        CategoryId = categoryId;
        BrandId = brandId;
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        UsageInstructions = usageInstructions;
        ImageUrl = imageUrl;
    }

    public void ChangeStatus(ProductStatus status) => Status = status;

    public ProductPackaging AddPackaging(
        Guid unitId,
        long conversionToBase,
        bool isBaseUnit,
        bool isPurchaseUnit,
        bool isSaleUnit,
        string status,
        string? packagingName = null,
        string? barcode = null)
    {
        if (isBaseUnit && _packagings.Any(p => !p.IsDeleted && p.IsBaseUnit))
        {
            throw new DomainException($"Product '{Sku}' already has a base packaging.");
        }

        if (_packagings.Any(p => !p.IsDeleted && p.UnitId == unitId))
        {
            throw new DomainException($"Product '{Sku}' already has a packaging for this unit.");
        }

        var packaging = new ProductPackaging(
            Id,
            unitId,
            conversionToBase,
            isBaseUnit,
            isPurchaseUnit,
            isSaleUnit,
            status,
            packagingName,
            barcode);

        _packagings.Add(packaging);

        return packaging;
    }
}
