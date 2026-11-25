using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Domain.Features.Inventory.Entities;

// The season a run is for: the crop, the selling window, the preparation days and the past window the history was
// read from (F4.7, DATABASE_DESIGN §35.25).
public sealed record SeasonalSuggestionSeason(
    string CropType,
    string? Region,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int PrepareDays,
    DateOnly ComparedFrom,
    DateOnly ComparedTo);

// One ranked product before it joins its run; the rank is its position in the list.
public sealed record SeasonalSuggestionLine(
    Guid ProductId,
    Guid? StoreProductId,
    SeasonalProductGroup Group,
    SeasonalSuggestionConfidence Confidence,
    string Reason,
    string EvidenceJson,
    long? SoldLastSeasonBase,
    long? SellableBase,
    long? SuggestedTargetBase,
    DateOnly? OrderByDate);

// The suggestion the reminder prepares for one upcoming crop season (F4.7): the products worth stocking, ranked from
// data, with their reason and evidence. Written once and never changed; it changes nothing else (no stock, seasonal
// rule, store product or goods receipt).
public sealed class SeasonalSuggestionRun : SoftDeletableEntity
{
    public const int MaxCropTypeLength = 50;
    public const int MaxRegionLength = 150;
    public const int MaxScheduleKeyLength = 120;
    public const int MaxPrepareDays = 90;

    // The longest selling window a run covers, first and last day included.
    public const int MaxSeasonDays = 183;

    // Ceiling of the Domain; the service asks for fewer.
    public const int MaxItems = 50;

    private readonly List<SeasonalSuggestionItem> _items = [];

    private SeasonalSuggestionRun()
    {
    }

    private SeasonalSuggestionRun(
        Guid storeId,
        string scheduleKey,
        SeasonalSuggestionSeason season,
        Guid? seasonalRuleId,
        Guid? cropSeasonId,
        int candidateCount,
        string candidatesJson,
        IReadOnlyList<SeasonalSuggestionLine> lines)
    {
        StoreId = Guard.NotEmpty(storeId);
        var key = Guard.NotNullOrWhiteSpace(scheduleKey).Trim();
        if (key.Length > MaxScheduleKeyLength)
            throw new DomainException($"The schedule key must be {MaxScheduleKeyLength} characters or fewer.");
        ScheduleKey = key;
        SetSeason(season);
        SeasonalRuleId = seasonalRuleId is { } rule ? Guard.NotEmpty(rule) : null;
        CropSeasonId = cropSeasonId is { } cropSeason ? Guard.NotEmpty(cropSeason) : null;
        if (candidateCount < 0) throw new DomainException("The candidate count cannot be negative.");
        CandidateCount = candidateCount;
        Candidates = Guard.NotNullOrWhiteSpace(candidatesJson);
        AddItems(lines);
    }

    public Guid StoreId { get; private set; }

    // One run per upcoming season: RULE:{rule}:{year} or SEASON:{crop season}.
    public string ScheduleKey { get; private set; } = null!;

    public string CropType { get; private set; } = null!;

    public string? Region { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public int PrepareDays { get; private set; }

    // The past window the sales and problem history were read from.
    public DateOnly ComparedFrom { get; private set; }

    public DateOnly ComparedTo { get; private set; }

    // Last year's seasonal rule that announced the season.
    public Guid? SeasonalRuleId { get; private set; }

    // The crop season that announced it.
    public Guid? CropSeasonId { get; private set; }

    public int CandidateCount { get; private set; }

    // The aggregated candidate data the ranking read (JSON); never personal data.
    public string Candidates { get; private set; } = null!;

    public IReadOnlyCollection<SeasonalSuggestionItem> Items => _items.AsReadOnly();

    public static SeasonalSuggestionRun Create(
        Guid storeId,
        string scheduleKey,
        SeasonalSuggestionSeason season,
        Guid? seasonalRuleId,
        Guid? cropSeasonId,
        int candidateCount,
        string candidatesJson,
        IReadOnlyList<SeasonalSuggestionLine> lines) =>
        new(storeId, scheduleKey, season, seasonalRuleId, cropSeasonId, candidateCount, candidatesJson, lines);

    private void SetSeason(SeasonalSuggestionSeason season)
    {
        var crop = Guard.NotNullOrWhiteSpace(season.CropType).Trim();
        if (crop.Length > MaxCropTypeLength)
            throw new DomainException($"The crop code must be {MaxCropTypeLength} characters or fewer.");
        var region = string.IsNullOrWhiteSpace(season.Region) ? null : season.Region.Trim();
        if (region is { Length: > MaxRegionLength })
            throw new DomainException($"The region must be {MaxRegionLength} characters or fewer.");
        if (season.EndsOn < season.StartsOn) throw new DomainException("The season must end on or after its first day.");
        if (season.EndsOn.DayNumber - season.StartsOn.DayNumber + 1 > MaxSeasonDays)
            throw new DomainException($"A season covers at most {MaxSeasonDays} days.");
        if (season.PrepareDays is < 0 or > MaxPrepareDays)
            throw new DomainException($"The preparation time must be between 0 and {MaxPrepareDays} days.");
        if (season.ComparedTo < season.ComparedFrom || season.ComparedTo >= season.StartsOn)
            throw new DomainException("The history window must be a past window before the season.");

        CropType = crop;
        Region = region;
        StartsOn = season.StartsOn;
        EndsOn = season.EndsOn;
        PrepareDays = season.PrepareDays;
        ComparedFrom = season.ComparedFrom;
        ComparedTo = season.ComparedTo;
    }

    private void AddItems(IReadOnlyList<SeasonalSuggestionLine> lines)
    {
        if (lines.Count > MaxItems) throw new DomainException($"A suggestion holds at most {MaxItems} products.");
        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            throw new DomainException("A product can be suggested only once in a run.");

        for (var index = 0; index < lines.Count; index++)
            _items.Add(new SeasonalSuggestionItem(Id, index + 1, lines[index]));
    }
}

// One ranked product of a run (F4.7). The reason and evidence are written by the server from data. A product the Store
// did not carry has no store product, sales or quantity.
public sealed class SeasonalSuggestionItem : SoftDeletableChildEntity
{
    public const int MaxReasonLength = 500;

    private SeasonalSuggestionItem()
    {
    }

    internal SeasonalSuggestionItem(Guid runId, int rank, SeasonalSuggestionLine line)
    {
        if (!Enum.IsDefined(line.Group)) throw new DomainException("Unknown product group.");
        if (!Enum.IsDefined(line.Confidence)) throw new DomainException("Unknown confidence level.");
        var reason = Guard.NotNullOrWhiteSpace(line.Reason).Trim();
        if (reason.Length > MaxReasonLength)
            throw new DomainException($"A reason must be {MaxReasonLength} characters or fewer.");
        if (line.SoldLastSeasonBase is < 0 || line.SuggestedTargetBase is < 0)
            throw new DomainException("Sold quantities and targets cannot be negative.");
        if (line.StoreProductId is null && (line.SoldLastSeasonBase is not null || line.SellableBase is not null
                                            || line.SuggestedTargetBase is not null))
            throw new DomainException("A product the store does not carry has no sales, stock or target.");

        RunId = runId;
        Rank = rank > 0 ? rank : throw new DomainException("The rank starts at 1.");
        ProductId = Guard.NotEmpty(line.ProductId);
        StoreProductId = line.StoreProductId is { } storeProduct ? Guard.NotEmpty(storeProduct) : null;
        ProductGroup = line.Group;
        Confidence = line.Confidence;
        Reason = reason;
        Evidence = Guard.NotNullOrWhiteSpace(line.EvidenceJson);
        SoldLastSeasonBase = line.SoldLastSeasonBase;
        SellableBase = line.SellableBase;
        SuggestedTargetBase = line.SuggestedTargetBase;
        OrderByDate = line.OrderByDate;
    }

    public Guid RunId { get; private set; }

    public Guid ProductId { get; private set; }

    // Null when the Store did not carry the product when the run was made.
    public Guid? StoreProductId { get; private set; }

    public int Rank { get; private set; }

    public SeasonalProductGroup ProductGroup { get; private set; }

    public SeasonalSuggestionConfidence Confidence { get; private set; }

    public string Reason { get; private set; } = null!;

    // JSON array of {kind, text}, written by the server.
    public string Evidence { get; private set; } = null!;

    // Base units sold (net of returns) in the history window.
    public long? SoldLastSeasonBase { get; private set; }

    // Sellable stock when the run was made.
    public long? SellableBase { get; private set; }

    // From the replenishment rules; null when the history is sparse.
    public long? SuggestedTargetBase { get; private set; }

    public DateOnly? OrderByDate { get; private set; }
}
