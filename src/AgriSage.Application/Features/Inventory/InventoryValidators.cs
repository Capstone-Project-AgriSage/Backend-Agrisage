using AgriSage.Application.Common;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Inventory.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Inventory;

public sealed class StockSummaryRequestValidator : AbstractValidator<StockSummaryRequest>
{
    public StockSummaryRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Search).MaximumLength(100);
    }
}

public sealed class InventoryAlertsRequestValidator : AbstractValidator<InventoryAlertsRequest>
{
    public InventoryAlertsRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.WithinDays).InclusiveBetween(1, 365);
        RuleFor(r => r.Type)
            .Must(t => t?.Trim().ToUpperInvariant() is "EXPIRING" or "EXPIRED" or "LOW_STOCK")
            .WithMessage("Type must be EXPIRING, EXPIRED or LOW_STOCK.")
            .When(r => !string.IsNullOrWhiteSpace(r.Type));
    }
}

public sealed class InventoryLotListRequestValidator : AbstractValidator<InventoryLotListRequest>
{
    public InventoryLotListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Status).Must(s => EnumText.TryParse<InventoryLotStatus>(s, out _))
            .WithMessage("Status must be ACTIVE, QUARANTINED, EXPIRED, BLOCKED or DEPLETED.")
            .When(r => !string.IsNullOrWhiteSpace(r.Status));
        RuleFor(r => r.Search).MaximumLength(100);
    }
}

public sealed class ChangeLotStatusRequestValidator : AbstractValidator<ChangeLotStatusRequest>
{
    public ChangeLotStatusRequestValidator()
    {
        RuleFor(r => r.Status)
            .Must(s => EnumText.TryParse<InventoryLotStatus>(s, out var status) && status != InventoryLotStatus.Depleted)
            .WithMessage("Status must be ACTIVE, QUARANTINED, EXPIRED or BLOCKED.");
    }
}

public sealed class StockMovementListRequestValidator : AbstractValidator<StockMovementListRequest>
{
    public StockMovementListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.Type).Must(t => EnumText.TryParse<StockMovementType>(t, out _))
            .WithMessage("Type must be STOCK_IN, SALE, RETURN_IN, ADJUSTMENT_IN, ADJUSTMENT_OUT or REVERSAL.")
            .When(r => !string.IsNullOrWhiteSpace(r.Type));
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate)
            .When(r => r.FromDate is not null && r.ToDate is not null)
            .WithMessage("ToDate must not be before FromDate.");
    }
}
