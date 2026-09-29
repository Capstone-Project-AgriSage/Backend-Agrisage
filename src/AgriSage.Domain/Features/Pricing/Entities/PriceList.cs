using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Pricing.Enums;

namespace AgriSage.Domain.Features.Pricing.Entities;

// "One active Walk-in default Price List per Store" and period overlap are enforced by
// Application + database constraints, not by this entity.
public sealed class PriceList : SoftDeletableEntity
{
    private PriceList()
    {
    }

    public PriceList(
        Guid storeId,
        string code,
        string name,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null,
        bool isWalkInDefault = false,
        string? description = null)
    {
        StoreId = storeId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Update(name, description);
        ChangeValidity(effectiveFrom, effectiveTo);
        IsWalkInDefault = isWalkInDefault;
        Status = PriceListStatus.Draft;
    }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public bool IsWalkInDefault { get; private set; }

    public PriceListStatus Status { get; private set; }

    public void Update(string name, string? description)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
    }

    public void ChangeValidity(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        Guard.ValidPeriod(effectiveFrom, effectiveTo);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public void SetWalkInDefault(bool isWalkInDefault) => IsWalkInDefault = isWalkInDefault;

    public void Activate()
    {
        if (Status == PriceListStatus.Active)
        {
            throw new DomainException($"Price list '{Code}' is already active.");
        }

        Status = PriceListStatus.Active;
    }

    public void Deactivate()
    {
        if (Status != PriceListStatus.Active)
        {
            throw new DomainException($"Only an active price list can be deactivated (current: {Status}).");
        }

        Status = PriceListStatus.Inactive;
    }
}
