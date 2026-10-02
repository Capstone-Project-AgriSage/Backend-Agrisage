using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Domain.Features.Products.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Products.Validators;

internal static class CatalogRuleExtensions
{
    public static IRuleBuilderOptions<T, string?> MustBeImageUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(1000).Must(Texts.IsHttpsUrl).WithMessage("Must be an absolute https URL.");

    public static IRuleBuilderOptions<T, string?> MustBeSearchText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(100);
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CategoryListRequestValidator : AbstractValidator<CategoryListRequest>
{
    public CategoryListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MustBeSearchText();
    }
}

public sealed class BrandRequestValidator : AbstractValidator<BrandRequest>
{
    public BrandRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Code).MaximumLength(50);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.LogoUrl).MustBeImageUrl().When(r => !string.IsNullOrWhiteSpace(r.LogoUrl));
    }
}

public sealed class BrandListRequestValidator : AbstractValidator<BrandListRequest>
{
    public BrandListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MustBeSearchText();
    }
}

public sealed class ActiveIngredientRequestValidator : AbstractValidator<ActiveIngredientRequest>
{
    public ActiveIngredientRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Code).MaximumLength(50);
    }
}

public sealed class ActiveIngredientListRequestValidator : AbstractValidator<ActiveIngredientListRequest>
{
    public ActiveIngredientListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MustBeSearchText();
    }
}

public sealed class UnitListRequestValidator : AbstractValidator<UnitListRequest>
{
    public UnitListRequestValidator() => Include(new PaginationRequestValidator());
}

public sealed class PackagingRequestValidator : AbstractValidator<PackagingRequest>
{
    public PackagingRequestValidator()
    {
        RuleFor(r => r.UnitId).NotEmpty();
        RuleFor(r => r.ConversionToBase).GreaterThan(0);
        RuleFor(r => r.ConversionToBase).Equal(1).When(r => r.IsBaseUnit)
            .WithMessage("A base packaging must have a conversion to base of 1.");
        RuleFor(r => r.PackagingName).MaximumLength(150);
        RuleFor(r => r.Barcode).MaximumLength(100);
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(r => r.Sku).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(255);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.ImageUrl).MustBeImageUrl().When(r => !string.IsNullOrWhiteSpace(r.ImageUrl));
        RuleFor(r => r.RequiresLotTracking).Equal(true)
            .When(r => r.RequiresExpiryDate)
            .WithMessage("A product that requires an expiry date must also require lot tracking.");

        RuleFor(r => r.Packagings).NotNull().NotEmpty().WithMessage("At least one packaging is required.");
        RuleForEach(r => r.Packagings).SetValidator(new PackagingRequestValidator());
        RuleFor(r => r.Packagings)
            .Must(p => p.Count(x => x.IsBaseUnit) == 1)
            .WithMessage("Exactly one base packaging is required.")
            .Must(p => p.Select(x => x.UnitId).Distinct().Count() == p.Count)
            .WithMessage("Each unit can be used by one packaging only.")
            .Must(p => HasNoDuplicateBarcodes(p.Select(x => x.Barcode)))
            .WithMessage("Barcodes must be unique.")
            .When(r => r.Packagings is { Count: > 0 });
    }

    internal static bool HasNoDuplicateBarcodes(IEnumerable<string?> barcodes)
    {
        var used = barcodes.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b!.Trim().ToLowerInvariant()).ToList();

        return used.Distinct().Count() == used.Count;
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(255);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.ImageUrl).MustBeImageUrl().When(r => !string.IsNullOrWhiteSpace(r.ImageUrl));
    }
}

public sealed class ChangeProductStatusRequestValidator : AbstractValidator<ChangeProductStatusRequest>
{
    public ChangeProductStatusRequestValidator()
    {
        RuleFor(r => r.Status).Must(s => Enum.TryParse<ProductStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be ACTIVE, INACTIVE or DISCONTINUED.");
    }
}

public sealed class UpdatePackagingRequestValidator : AbstractValidator<UpdatePackagingRequest>
{
    public UpdatePackagingRequestValidator()
    {
        RuleFor(r => r.Status).Must(s => PackagingStatus.IsValid(s)).WithMessage("Status must be ACTIVE or INACTIVE.");
        RuleFor(r => r.PackagingName).MaximumLength(150);
        RuleFor(r => r.Barcode).MaximumLength(100);
    }
}

public sealed class SetProductIngredientsRequestValidator : AbstractValidator<SetProductIngredientsRequest>
{
    public SetProductIngredientsRequestValidator()
    {
        RuleFor(r => r.Ingredients).NotNull();
        RuleForEach(r => r.Ingredients).ChildRules(item =>
        {
            item.RuleFor(i => i.ActiveIngredientId).NotEmpty();
            item.RuleFor(i => i.Concentration).MaximumLength(100);
            item.RuleFor(i => i.Note).MaximumLength(500);
        });
        RuleFor(r => r.Ingredients)
            .Must(list => list.Select(i => i.ActiveIngredientId).Distinct().Count() == list.Count)
            .WithMessage("An ingredient can be listed once.")
            .When(r => r.Ingredients is not null);
    }
}

public sealed class ProductListRequestValidator : AbstractValidator<ProductListRequest>
{
    public ProductListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => Enum.TryParse<ProductStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be ACTIVE, INACTIVE or DISCONTINUED.")
            .When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.Search).MustBeSearchText();
    }
}

public sealed class CreateStoreProductRequestValidator : AbstractValidator<CreateStoreProductRequest>
{
    public CreateStoreProductRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.StoreSku).MaximumLength(50);
        RuleFor(r => r.MinStockLevelBase).GreaterThanOrEqualTo(0).When(r => r.MinStockLevelBase is not null);
    }
}

public sealed class UpdateStoreProductRequestValidator : AbstractValidator<UpdateStoreProductRequest>
{
    public UpdateStoreProductRequestValidator()
    {
        RuleFor(r => r.StoreSku).MaximumLength(50);
        RuleFor(r => r.MinStockLevelBase).GreaterThanOrEqualTo(0).When(r => r.MinStockLevelBase is not null);
    }
}

public sealed class StoreProductListRequestValidator : AbstractValidator<StoreProductListRequest>
{
    public StoreProductListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MustBeSearchText();
    }
}

public sealed class CatalogProductListRequestValidator : AbstractValidator<CatalogProductListRequest>
{
    public CatalogProductListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MustBeSearchText();
    }
}
