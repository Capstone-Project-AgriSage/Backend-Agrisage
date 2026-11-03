using AgriSage.Application.Common.Models;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.19: closing a season (D11), its debt report (D9) and credit-limit suggestions (D12, CS-14).

public sealed record SeasonOpenEntry(Guid EntryId, string EntryNumber, Guid FarmerProfileId, string FullName,
    decimal OutstandingAmount, string Status);

public sealed record SeasonOpenOrder(Guid OrderId, string OrderNumber, string Status);

// Reasons: ALREADY_CLOSED, HARVEST_NOT_ENDED (the season is not collecting yet), OPEN_ENTRIES (debt still collected in the
// season, disputed included), OPEN_CREDIT_ORDERS (credit orders of the season not completed or cancelled). The lists show
// at most 50 items each; the counts are complete.
public sealed record CropSeasonClosingCheckResponse(
    Guid SeasonId,
    string Status,
    bool CanClose,
    IReadOnlyList<string> Reasons,
    int OpenEntryCount,
    IReadOnlyList<SeasonOpenEntry> OpenEntries,
    int OpenCreditOrderCount,
    IReadOnlyList<SeasonOpenOrder> OpenCreditOrders);

public sealed record CloseCropSeasonRequest(long Version, string? Note = null);

// Debt that arose in the season (origin, D9). originated − collected − returned − reduced − cancelled = outstanding;
// carriedOver is the part of outstanding now collected in a later season.
public sealed record SeasonOriginFigures(
    decimal OriginatedAmount,
    decimal CollectedAmount,
    decimal ReturnedAmount,
    decimal ReducedAmount,
    decimal CancelledAmount,
    decimal CarriedOverAmount,
    decimal OutstandingAmount,
    int EntryCount,
    int CustomerCount);

// Debt collected in the season now: carriedIn arose in another season (or had none) and was moved here.
public sealed record SeasonCollectionFigures(decimal CarriedInAmount, decimal ToCollectAmount, int OpenEntryCount);

// IsFrozen: a closed season shows the figures recorded when it closed (closed_summary).
public sealed record DebtBySeasonItem(
    DebtSeasonReference Season,
    DateOnly HarvestStart,
    DateOnly PaymentDueDate,
    string Status,
    DateTimeOffset? ClosedAt,
    bool IsFrozen,
    SeasonOriginFigures Origin,
    SeasonCollectionFigures Collection);

public sealed record DebtBySeasonRequest : PaginationRequest
{
    // Seasons whose harvest starts in this year.
    public int? Year { get; init; }
}

public sealed record LimitSuggestionItem(
    Guid FarmerProfileId,
    string FullName,
    string? PhoneNumber,
    decimal CreditLimit,
    string CreditStatus,
    decimal PaidAmount,
    DateOnly LastPaidOn);

// Seasons: the closed seasons checked (empty when fewer than RequiredSeasons exist up to this one). Read-only: the owner
// raises a limit through the existing credit-limit API.
public sealed record LimitSuggestionsResponse(
    Guid SeasonId,
    int RequiredSeasons,
    IReadOnlyList<DebtSeasonReference> Seasons,
    IReadOnlyList<LimitSuggestionItem> Items);

public interface ICropSeasonClosingService
{
    Task<CropSeasonClosingCheckResponse> ClosingCheckAsync(Guid seasonId, CancellationToken token);
    Task<CropSeasonResponse> CloseAsync(Guid seasonId, CloseCropSeasonRequest request, CancellationToken token);
    Task<LimitSuggestionsResponse> LimitSuggestionsAsync(Guid seasonId, CancellationToken token);
    Task<PagedResult<DebtBySeasonItem>> ReportAsync(DebtBySeasonRequest request, CancellationToken token);
}

public sealed class CloseCropSeasonRequestValidator : AbstractValidator<CloseCropSeasonRequest>
{
    public CloseCropSeasonRequestValidator()
    {
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class DebtBySeasonRequestValidator : AbstractValidator<DebtBySeasonRequest>
{
    public DebtBySeasonRequestValidator()
    {
        Include(new Common.Validators.PaginationRequestValidator());
        RuleFor(r => r.Year).InclusiveBetween(2000, 2100).When(r => r.Year.HasValue);
    }
}
