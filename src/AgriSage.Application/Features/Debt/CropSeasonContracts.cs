using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Debt.Entities;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.11: debt-collection crop seasons (CS-04, D1-D3, D16). Field names follow D16: the debt of a season is reported as
// "originated" (debt that arose in it, for reports) and "collecting" (debt collected in it now, for closing).
public sealed record CropSeasonListRequest : PaginationRequest
{
    // Seasons whose harvest starts in this year.
    public int? Year { get; init; }

    // PLANNED | OPEN | HARVESTING | COLLECTING | CLOSED, derived from today's Vietnam date (D2).
    public string? Status { get; init; }
}

public sealed record CropSeasonDebtFigures(decimal OutstandingAmount, int OpenEntryCount);

public sealed record CropSeasonResponse(
    Guid Id,
    string Code,
    string Name,
    string CropType,
    DateOnly OrderWindowStart,
    DateOnly HarvestStart,
    DateOnly HarvestEnd,
    int GraceDays,
    DateOnly PaymentDueDate,
    string Status,
    Guid? SeasonalRuleId,
    DateTimeOffset? ClosedAt,
    long Version,
    CropSeasonDebtFigures Originated,
    CropSeasonDebtFigures Collecting,
    DebtExtensionResult? DebtExtension = null);

// Only on the response of an update that asked to apply the new due date to the debts being collected: how many entries moved
// and how many disputed ones were left on the old date (the owner decides those).
public sealed record DebtExtensionResult(int Moved, int SkippedDisputed);

// OrderWindowStart is only given for the store's first season; later seasons open on the previous harvest start (D3).
// GraceDays defaults to the store debt policy (30 when the store has none).
public sealed record CreateCropSeasonRequest(
    string Code,
    string Name,
    DateOnly HarvestStart,
    DateOnly HarvestEnd,
    int? GraceDays = null,
    DateOnly? OrderWindowStart = null,
    string? CropType = null,
    Guid? SeasonalRuleId = null);

// ApplyNewDueDateToCollectingDebts (owner only): when the payment due date moves later, entries collected in this season
// that still follow its old due date move with it (CHANGE_DUE_DATE action + audit). By default recorded debt keeps its due
// date (D17). Reason is required when applying.
public sealed record UpdateCropSeasonRequest(
    string Name,
    DateOnly HarvestStart,
    DateOnly HarvestEnd,
    int GraceDays,
    long Version,
    Guid? SeasonalRuleId = null,
    bool ApplyNewDueDateToCollectingDebts = false,
    string? Reason = null);

// The season a Farmer's credit order placed today would belong to, with its due date. Only an estimate: debt is created
// on delivery (D15). DueRule: SEASON, CAPPED (store term cap), TERM_DAYS (credit-tier term, no season) or NONE (no credit).
public sealed record MyCurrentCropSeasonResponse(
    Guid? CropSeasonId,
    string? Code,
    string? Name,
    DateOnly? HarvestStart,
    DateOnly? HarvestEnd,
    DateOnly? ExpectedDueDate,
    string DueRule,
    int? TermDays,
    bool IsEstimate);

public interface ICropSeasonService
{
    Task<PagedResult<CropSeasonResponse>> ListAsync(CropSeasonListRequest request, CancellationToken token);
    Task<CropSeasonResponse> GetAsync(Guid id, CancellationToken token);
    Task<CropSeasonResponse> CreateAsync(CreateCropSeasonRequest request, CancellationToken token);
    Task<CropSeasonResponse> UpdateAsync(Guid id, UpdateCropSeasonRequest request, CancellationToken token);
    Task<MyCurrentCropSeasonResponse> MyCurrentAsync(CancellationToken token);
}

public sealed class CropSeasonListRequestValidator : AbstractValidator<CropSeasonListRequest>
{
    private static readonly string[] Statuses = ["PLANNED", "OPEN", "HARVESTING", "COLLECTING", "CLOSED"];

    public CropSeasonListRequestValidator()
    {
        Include(new AgriSage.Application.Common.Validators.PaginationRequestValidator());
        RuleFor(r => r.Year).InclusiveBetween(2000, 2100).When(r => r.Year.HasValue);
        RuleFor(r => r.Status).Must(s => s is null || Statuses.Contains(s)).WithMessage("Unknown season status.");
    }
}

public sealed class CreateCropSeasonRequestValidator : AbstractValidator<CreateCropSeasonRequest>
{
    public CreateCropSeasonRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(30);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.CropType).MaximumLength(50);
        RuleFor(r => r.HarvestEnd).GreaterThan(r => r.HarvestStart);
        RuleFor(r => r.GraceDays).InclusiveBetween(0, CropSeason.MaxGraceDays).When(r => r.GraceDays.HasValue);
        RuleFor(r => r.OrderWindowStart).LessThan(r => r.HarvestStart).When(r => r.OrderWindowStart.HasValue);
        RuleFor(r => r.SeasonalRuleId).NotEqual(Guid.Empty).When(r => r.SeasonalRuleId.HasValue);
    }
}

public sealed class UpdateCropSeasonRequestValidator : AbstractValidator<UpdateCropSeasonRequest>
{
    public UpdateCropSeasonRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(150);
        RuleFor(r => r.HarvestEnd).GreaterThan(r => r.HarvestStart);
        RuleFor(r => r.GraceDays).InclusiveBetween(0, CropSeason.MaxGraceDays);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.SeasonalRuleId).NotEqual(Guid.Empty).When(r => r.SeasonalRuleId.HasValue);
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(1000).When(r => r.ApplyNewDueDateToCollectingDebts);
    }
}
