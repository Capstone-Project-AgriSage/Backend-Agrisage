using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Stocktakes;

public sealed class CreateStocktakeRequestValidator : AbstractValidator<CreateStocktakeRequest>
{
    public CreateStocktakeRequestValidator()
    {
        RuleFor(r => r.Note).MaximumLength(1000);
        RuleForEach(r => r.StoreProductIds).NotEmpty();
        RuleFor(r => r.StoreProductIds).Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Store product ids must be distinct.");
    }
}

public sealed class StocktakeCountRequestValidator : AbstractValidator<StocktakeCountRequest>
{
    public StocktakeCountRequestValidator()
    {
        RuleFor(r => r.ItemId).NotEmpty();
        RuleFor(r => r.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(r => r.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(20, 6, true);
        RuleFor(r => r.ReasonCode).Must(StockAdjustmentReasons.IsStocktake).When(r => !string.IsNullOrWhiteSpace(r.ReasonCode))
            .WithMessage("Invalid stocktake reason.");
        RuleFor(r => r.Note).MaximumLength(500);
    }
}

public sealed class StocktakeCountsRequestValidator : AbstractValidator<StocktakeCountsRequest>
{
    public StocktakeCountsRequestValidator()
    {
        RuleFor(r => r.Counts).NotEmpty();
        RuleForEach(r => r.Counts).NotNull().SetValidator(new StocktakeCountRequestValidator());
        RuleFor(r => r.Counts).Must(lines => lines.Select(l => l.ItemId).Distinct().Count() == lines.Count)
            .When(r => r.Counts is not null && r.Counts.All(l => l is not null))
            .WithMessage("Each stocktake item may appear only once.");
    }
}

public sealed class CancelStocktakeRequestValidator : AbstractValidator<CancelStocktakeRequest>
{
    public CancelStocktakeRequestValidator() => RuleFor(r => r.Reason).MaximumLength(1000);
}

public sealed class StocktakeListRequestValidator : AbstractValidator<StocktakeListRequest>
{
    public StocktakeListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<StocktakeStatus>(s, out _))
            .When(r => !string.IsNullOrWhiteSpace(r.Status)).WithMessage("Status must be DRAFT, IN_PROGRESS, COMPLETED or CANCELLED.");
        RuleFor(r => r.Search).MaximumLength(100);
        RuleFor(r => r.FromDate).GreaterThan(DateOnly.MinValue);
        RuleFor(r => r.ToDate).GreaterThan(DateOnly.MinValue);
        RuleFor(r => r.ToDate).LessThan(DateOnly.MaxValue);
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate).When(r => r.FromDate is not null && r.ToDate is not null);
    }
}
