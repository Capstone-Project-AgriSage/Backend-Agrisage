using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// A debt-collection crop season entered by the store owner each year (CS-04, decisions D1–D3, D9, D11). Credit orders
// confirmed inside its order window [OrderWindowStart, HarvestStart) belong to it, and their debt is due on
// PaymentDueDate = HarvestEnd + GraceDays. The phase is derived from the date; only closing is recorded. A closed season
// never changes and never reopens. SeasonalRuleId optionally links the inventory seasonal rule of the same season so
// stock and debt can be reported together; the two calendars stay independent (FLOW_4).
public sealed class CropSeason : SoftDeletableEntity, IHasConcurrencyVersion
{
    public const int MaxGraceDays = 365;

    private CropSeason()
    {
    }

    public CropSeason(
        Guid storeId,
        string code,
        string name,
        string cropType,
        DateOnly orderWindowStart,
        DateOnly harvestStart,
        DateOnly harvestEnd,
        int graceDays,
        Guid? seasonalRuleId = null)
    {
        StoreId = storeId;
        Code = Guard.NotNullOrWhiteSpace(code).Trim();
        CropType = Guard.NotNullOrWhiteSpace(cropType).Trim();
        OrderWindowStart = orderWindowStart;
        Update(name, harvestStart, harvestEnd, graceDays);
        LinkSeasonalRule(seasonalRuleId);
    }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string CropType { get; private set; } = null!;

    public DateOnly OrderWindowStart { get; private set; }

    public DateOnly HarvestStart { get; private set; }

    public DateOnly HarvestEnd { get; private set; }

    public int GraceDays { get; private set; }

    public DateOnly PaymentDueDate { get; private set; }

    public Guid? SeasonalRuleId { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedBy { get; private set; }

    // Figures frozen when the owner closes the season (jsonb), so later corrections never rewrite a closed season's report.
    public string? ClosedSummary { get; private set; }

    public long Version { get; private set; }

    public bool IsClosed => ClosedAt is not null;

    public void Update(string name, DateOnly harvestStart, DateOnly harvestEnd, int graceDays)
    {
        EnsureOpen();
        if (harvestEnd <= harvestStart)
        {
            throw new DomainException("The harvest must end after it starts.");
        }

        if (OrderWindowStart >= harvestStart)
        {
            throw new DomainException("The order window must open before the harvest starts.");
        }

        if (graceDays is < 0 or > MaxGraceDays)
        {
            throw new DomainException($"Grace days must be between 0 and {MaxGraceDays}.");
        }

        Name = Guard.NotNullOrWhiteSpace(name).Trim();
        HarvestStart = harvestStart;
        HarvestEnd = harvestEnd;
        GraceDays = graceDays;
        PaymentDueDate = harvestEnd.AddDays(graceDays);
    }

    // Seasons chain without gaps: the application moves this start when the previous season's harvest start changes.
    public void MoveOrderWindowStart(DateOnly orderWindowStart)
    {
        EnsureOpen();
        if (orderWindowStart >= HarvestStart)
        {
            throw new DomainException("The order window must open before the harvest starts.");
        }

        OrderWindowStart = orderWindowStart;
    }

    public void LinkSeasonalRule(Guid? seasonalRuleId)
    {
        EnsureOpen();
        if (seasonalRuleId == Guid.Empty)
        {
            throw new DomainException("A linked seasonal rule needs an id.");
        }

        SeasonalRuleId = seasonalRuleId;
    }

    public CropSeasonPhase PhaseOn(DateOnly today) =>
        IsClosed ? CropSeasonPhase.Closed
        : today < OrderWindowStart ? CropSeasonPhase.Planned
        : today < HarvestStart ? CropSeasonPhase.Open
        : today <= HarvestEnd ? CropSeasonPhase.Harvesting
        : CropSeasonPhase.Collecting;

    // D11: only a collecting season closes. The application checks first that no debt is still collected in this season
    // and no credit order of it is still open.
    public void Close(Guid closedBy, DateTimeOffset closedAt, string summary, DateOnly today)
    {
        EnsureOpen();
        if (PhaseOn(today) != CropSeasonPhase.Collecting)
        {
            throw new DomainException($"Season '{Code}' can only be closed after its harvest has ended.");
        }

        ClosedBy = Guard.NotEmpty(closedBy);
        ClosedAt = closedAt;
        ClosedSummary = Guard.NotNullOrWhiteSpace(summary);
    }

    // Seasons are reference data for debt history: they are closed, never deleted.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Crop seasons cannot be deleted; close them instead.");

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainException($"Season '{Code}' is closed and can no longer change.");
        }
    }
}
