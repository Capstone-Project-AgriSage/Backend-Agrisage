using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.Domain.Features.Advisory.Entities;

// Reviewed link between a problem and a product (api-flows/ADVISORY.md V1, V5). A link is created as a draft, a person
// approves it, and only an approved link reaches a Farmer. Changing the content of an approved link returns it to draft.
public sealed class DiseaseProduct : SoftDeletableEntity
{
    public const int MaxApplications = 10;

    private DiseaseProduct()
    {
    }

    public DiseaseProduct(
        Guid diseaseId,
        Guid productId,
        RecommendationGroup group,
        string doseText,
        string? registrationSource = null,
        string? reason = null,
        string? doseSource = null,
        decimal? dosePerHaBase = null,
        int applications = 1,
        int? preHarvestDays = null,
        IReadOnlyCollection<GrowthStage>? growthStages = null,
        int priority = 0)
    {
        DiseaseId = diseaseId;
        ProductId = productId;
        Status = DiseaseProductStatus.Draft;
        Apply(group, doseText, registrationSource, reason, doseSource, dosePerHaBase, applications, preHarvestDays, growthStages, priority);
    }

    public Guid DiseaseId { get; private set; }

    public Disease Disease { get; private set; } = null!;

    public Guid ProductId { get; private set; }

    public Product Product { get; private set; } = null!;

    public RecommendationGroup RecommendationGroup { get; private set; }

    // Crop protection: the document and page that register the product for this problem on rice.
    public string? RegistrationSource { get; private set; }

    // Nutrition: why the product helps with this problem.
    public string? Reason { get; private set; }

    public string DoseText { get; private set; } = null!;

    public string? DoseSource { get; private set; }

    // Base units of the product per hectare and per application; null when the quantity cannot be computed.
    public decimal? DosePerHaBase { get; private set; }

    public int Applications { get; private set; } = 1;

    public int? PreHarvestDays { get; private set; }

    // Comma separated stage codes; null = every stage.
    public string? GrowthStages { get; private set; }

    public int Priority { get; private set; }

    public DiseaseProductStatus Status { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public IReadOnlyList<GrowthStage> StageList => GrowthStages is null ? [] : ParseStages(GrowthStages);

    // Changing an approved link sends it back to review: nobody approved the new content.
    public void Update(
        RecommendationGroup group,
        string doseText,
        string? registrationSource,
        string? reason,
        string? doseSource,
        decimal? dosePerHaBase,
        int applications,
        int? preHarvestDays,
        IReadOnlyCollection<GrowthStage>? growthStages,
        int priority)
    {
        Apply(group, doseText, registrationSource, reason, doseSource, dosePerHaBase, applications, preHarvestDays, growthStages, priority);

        if (Status == DiseaseProductStatus.Approved)
        {
            Status = DiseaseProductStatus.Draft;
            ApprovedBy = null;
            ApprovedAt = null;
        }
    }

    public void Approve(Guid approvedBy, DateTimeOffset approvedAt)
    {
        if (approvedBy == Guid.Empty)
        {
            throw new DomainException("An approver is required.");
        }

        if (Status == DiseaseProductStatus.Approved)
        {
            throw new DomainException("The link is already approved.");
        }

        Status = DiseaseProductStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
    }

    public void Deactivate()
    {
        Status = DiseaseProductStatus.Inactive;
        ApprovedBy = null;
        ApprovedAt = null;
    }

    // True when the link fits the growth stage the Farmer gave: no stage given, no stage limit, or the stage is listed.
    public bool FitsStage(GrowthStage? stage) => stage is null || GrowthStages is null || StageList.Contains(stage.Value);

    public static IReadOnlyList<GrowthStage> ParseStages(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => Enum.TryParse<GrowthStage>(code.Replace("_", string.Empty), ignoreCase: true, out var stage)
                ? stage
                : throw new DomainException($"Unknown growth stage '{code}'."))
            .Distinct()
            .ToList();

    private void Apply(
        RecommendationGroup group,
        string doseText,
        string? registrationSource,
        string? reason,
        string? doseSource,
        decimal? dosePerHaBase,
        int applications,
        int? preHarvestDays,
        IReadOnlyCollection<GrowthStage>? growthStages,
        int priority)
    {
        DoseText = Guard.NotNullOrWhiteSpace(doseText).Trim();

        if (group == RecommendationGroup.Protection && string.IsNullOrWhiteSpace(registrationSource))
        {
            throw new DomainException("A crop-protection link needs its registration source (document and page).");
        }

        if (group == RecommendationGroup.Nutrition && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A nutrition link needs the reason the product helps.");
        }

        if (dosePerHaBase is <= 0)
        {
            throw new DomainException("The dose per hectare must be greater than zero.");
        }

        if (applications is < 1 or > MaxApplications)
        {
            throw new DomainException($"The number of applications must be between 1 and {MaxApplications}.");
        }

        if (preHarvestDays is < 0)
        {
            throw new DomainException("The pre-harvest interval must not be negative.");
        }

        RecommendationGroup = group;
        RegistrationSource = string.IsNullOrWhiteSpace(registrationSource) ? null : registrationSource.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DoseSource = string.IsNullOrWhiteSpace(doseSource) ? null : doseSource.Trim();
        DosePerHaBase = dosePerHaBase;
        Applications = applications;
        PreHarvestDays = preHarvestDays;
        GrowthStages = growthStages is { Count: > 0 }
            ? string.Join(',', growthStages.Distinct().OrderBy(stage => (int)stage).Select(stage => stage.ToString().ToUpperInvariant()))
            : null;
        Priority = priority;
    }
}
