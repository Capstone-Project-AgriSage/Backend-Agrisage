using System.ComponentModel.DataAnnotations;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;
using AgriSage.Domain.Features.Inventory.Entities;
using FluentValidation;

namespace AgriSage.Application.Features.Inventory;

// F4.7 seasonal product suggestions (FLOW_4 "Seasonal product suggestions", DATABASE_DESIGN §35.25).
// Section "SeasonalSuggestions" of the configuration.
public sealed class SeasonalSuggestionOptions
{
    public const string SectionName = "SeasonalSuggestions";

    // Candidates the ranking may choose from: the Store's products first, then up to MaxNotCarried
    // catalog products the Store does not carry.
    [Range(10, 500)] public int MaxCandidates { get; set; } = 200;

    [Range(0, 200)] public int MaxNotCarried { get; set; } = 50;

    // Products kept in a run.
    [Range(1, SeasonalSuggestionRun.MaxItems)] public int MaxItems { get; set; } = 20;

    // The reminder prepares a run this many days before a season starts and reminds once more at the second mark.
    [Range(1, 90)] public int FirstReminderDays { get; set; } = 30;

    [Range(1, 90)] public int SecondReminderDays { get; set; } = 14;

    // Upcoming seasons handled per reminder run.
    [Range(1, 50)] public int MaxSeasonsPerRun { get; set; } = 5;
}

public sealed record SeasonalSuggestionListRequest : PaginationRequest
{
    public string? CropType { get; init; }

    // Seasons that overlap these days.
    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }
}

// Kind: SALES, LINK, DIAGNOSIS, ADVISORY, RULE, STOCK; the text is written by the server.
public sealed record SeasonalSuggestionEvidence(string Kind, string Text);

public sealed record SeasonalSuggestionItemResponse(
    Guid Id,
    int Rank,
    Guid ProductId,
    Guid? StoreProductId,
    bool InStore,
    string Sku,
    string ProductName,
    string BaseUnit,
    string Group,
    string Confidence,
    string Reason,
    IReadOnlyList<SeasonalSuggestionEvidence> Evidence,
    long? SoldLastSeasonBase,
    long? SellableBase,
    long? SuggestedTargetBase,
    DateOnly? OrderByDate);

// An active seasonal rule of the same crop whose days overlap the run's season: the season is planned already.
public sealed record SeasonalCoveringRule(Guid Id, string Name);

public sealed record SeasonalSuggestionResponse(
    Guid Id,
    string CropType,
    string CropName,
    string? Region,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int PrepareDays,
    DateOnly ComparedFrom,
    DateOnly ComparedTo,
    int CandidateCount,
    Guid? SeasonalRuleId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SeasonalSuggestionItemResponse> Items,
    SeasonalCoveringRule? CoveringRule);

public sealed record SeasonalSuggestionListItem(
    Guid Id,
    string CropType,
    string CropName,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int ItemCount,
    DateTimeOffset CreatedAt,
    SeasonalCoveringRule? CoveringRule);

// A seasonal rule filled in from a suggestion (FLOW_4 "Seasonal product suggestions", rule 6). Nothing is saved: the
// owner checks it and saves it with POST /api/inventory/seasonal-rules.
public sealed record SeasonalRuleDraftLine(Guid StoreProductId, string Sku, string ProductName, string BaseUnit,
    long? TargetStockLevelBase);

// A suggested product left out of the draft. Reason: NOT_CARRIED (the store must add it first).
public sealed record SeasonalRuleDraftSkipped(Guid ProductId, string Sku, string ProductName, string Reason);

public sealed record SeasonalRuleDraftResponse(
    Guid SuggestionId,
    string Name,
    string CropType,
    string CropName,
    string? Region,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int PrepareDays,
    decimal DemandFactor,
    // LAST_SEASON (from last year's sales) or DEFAULT (1.5, no product with enough history).
    string DemandFactorBasis,
    int DemandFactorProducts,
    DateOnly ComparedFrom,
    DateOnly ComparedTo,
    DateOnly BaselineFrom,
    DateOnly BaselineTo,
    SeasonalCoveringRule? CoveringRule,
    IReadOnlyList<SeasonalRuleDraftLine> Items,
    IReadOnlyList<SeasonalRuleDraftSkipped> Skipped);

public interface ISeasonalSuggestionService
{
    Task<PagedResult<SeasonalSuggestionListItem>> ListAsync(SeasonalSuggestionListRequest request, CancellationToken token);

    Task<SeasonalSuggestionResponse> GetAsync(Guid id, CancellationToken token);

    Task<SeasonalRuleDraftResponse> RuleDraftAsync(Guid id, CancellationToken token);
}

public sealed class SeasonalSuggestionListRequestValidator : AbstractValidator<SeasonalSuggestionListRequest>
{
    public SeasonalSuggestionListRequestValidator()
    {
        Include(new PaginationRequestValidator());
        RuleFor(r => r.CropType).MaximumLength(SeasonalSuggestionRun.MaxCropTypeLength);
        RuleFor(r => r).Must(r => r.FromDate is null || r.ToDate is null || r.ToDate >= r.FromDate)
            .WithMessage("End date must not precede start date.");
    }
}
