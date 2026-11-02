using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Advisory.Enums;

namespace AgriSage.Domain.Features.Advisory.Entities;

// One recommendation given to a Farmer: a log for tracing and statistics, not a cart (api-flows/ADVISORY.md V11).
public sealed class AdvisorySession : SoftDeletableEntity
{
    public const decimal MaxAreaSqm = 1_000_000m;

    private AdvisorySession()
    {
    }

    public AdvisorySession(
        Guid storeId,
        Guid farmerProfileId,
        Guid diseaseId,
        AdvisorySource source,
        Guid? diagnosisCaseId,
        string? queryText,
        decimal? areaSqm,
        GrowthStage? growthStage)
    {
        StoreId = Guard.NotEmpty(storeId);
        FarmerProfileId = Guard.NotEmpty(farmerProfileId);
        DiseaseId = Guard.NotEmpty(diseaseId);
        Refresh(source, diagnosisCaseId, queryText, areaSqm, growthStage);
    }

    public Guid StoreId { get; private set; }

    public Guid FarmerProfileId { get; private set; }

    public Guid DiseaseId { get; private set; }

    public AdvisorySource Source { get; private set; }

    public Guid? DiagnosisCaseId { get; private set; }

    public string? QueryText { get; private set; }

    public decimal? AreaSqm { get; private set; }

    public GrowthStage? GrowthStage { get; private set; }

    // A later call for the same Farmer and problem within the reuse window updates this session (V11).
    public void Refresh(AdvisorySource source, Guid? diagnosisCaseId, string? queryText, decimal? areaSqm, GrowthStage? growthStage)
    {
        if (areaSqm is <= 0 or > MaxAreaSqm)
        {
            throw new DomainException($"The area must be greater than zero and at most {MaxAreaSqm:0} square metres.");
        }

        Source = source;
        DiagnosisCaseId = diagnosisCaseId;
        QueryText = string.IsNullOrWhiteSpace(queryText) ? null : queryText.Trim();
        AreaSqm = areaSqm;
        GrowthStage = growthStage;
    }
}
