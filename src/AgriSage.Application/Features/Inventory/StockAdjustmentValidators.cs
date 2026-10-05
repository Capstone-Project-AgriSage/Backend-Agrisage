using FluentValidation;

namespace AgriSage.Application.Features.Inventory;

public static class StockAdjustmentReasons
{
    public static bool IsManual(string? value) => value?.Trim().ToUpperInvariant()
        is "DAMAGED" or "EXPIRED" or "LOST" or "MANUAL_CORRECTION" or "OTHER";
    public static bool IsStocktake(string? value) => IsManual(value)
        || string.Equals(value?.Trim(), "STOCKTAKE_DIFFERENCE", StringComparison.OrdinalIgnoreCase);
}

public sealed class StockAdjustmentLineValidator : AbstractValidator<StockAdjustmentLine>
{
    public StockAdjustmentLineValidator()
    {
        RuleFor(r => r.InventoryLotId).NotEmpty();
        RuleFor(r => r.QuantityDeltaBase).NotEqual(0).GreaterThan(long.MinValue);
        RuleFor(r => r.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(20, 6, true);
    }
}

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(r => r.ReasonCode).Must(StockAdjustmentReasons.IsManual).WithMessage("Invalid stock adjustment reason.");
        RuleFor(r => r.Note).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.Lines).NotEmpty();
        RuleForEach(r => r.Lines).NotNull().SetValidator(new StockAdjustmentLineValidator());
        When(r => r.Lines is { Count: > 0 } && r.Lines.All(l => l is not null), () =>
        {
            RuleFor(r => r.Lines).Must(lines => lines.All(l => l.QuantityDeltaBase > 0) || lines.All(l => l.QuantityDeltaBase < 0))
                .WithMessage("All quantity deltas must have the same sign.");
            RuleFor(r => r.Lines).Must(lines => lines.Select(l => l.InventoryLotId).Distinct().Count() == lines.Count)
                .WithMessage("Each inventory lot may appear only once.");
        });
    }
}
