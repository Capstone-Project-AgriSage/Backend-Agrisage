using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Pricing.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Pricing;

public sealed class PriceListRequestValidator : AbstractValidator<PriceListRequest>
{
    public PriceListRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(50);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.EffectiveTo).GreaterThan(r => r.EffectiveFrom).When(r => r.EffectiveTo is not null)
            .WithMessage("EffectiveTo must be after EffectiveFrom.");
    }
}

public sealed class UpdatePriceListRequestValidator : AbstractValidator<UpdatePriceListRequest>
{
    public UpdatePriceListRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.Description).MaximumLength(500);
        RuleFor(r => r.EffectiveTo).GreaterThan(r => r.EffectiveFrom).When(r => r.EffectiveTo is not null)
            .WithMessage("EffectiveTo must be after EffectiveFrom.");
    }
}

public sealed class PriceListListRequestValidator : AbstractValidator<PriceListListRequest>
{
    public PriceListListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<PriceListStatus>(s, out _))
            .WithMessage("Status must be DRAFT, ACTIVE or INACTIVE.")
            .When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.Search).MaximumLength(100);
    }
}

public sealed class PriceListItemListRequestValidator : AbstractValidator<PriceListItemListRequest>
{
    public PriceListItemListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
    }
}

public sealed class UpsertPriceListItemsRequestValidator : AbstractValidator<UpsertPriceListItemsRequest>
{
    public const int MaxItems = 500;

    public UpsertPriceListItemsRequestValidator()
    {
        RuleFor(r => r.Items).NotEmpty()
            .Must(items => items is null || items.Count <= MaxItems).WithMessage($"At most {MaxItems} lines per call.")
            .Must(items => items is null
                || items.Select(i => (i.StoreProductId, i.ProductPackagingId)).Distinct().Count() == items.Count)
            .WithMessage("Each (storeProductId, productPackagingId) pair may appear only once.");
        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.StoreProductId).NotEmpty();
            item.RuleFor(i => i.ProductPackagingId).NotEmpty();
            item.RuleFor(i => i.SellingPrice).MustBeMoney();
        }).When(r => r.Items is not null);
    }
}
