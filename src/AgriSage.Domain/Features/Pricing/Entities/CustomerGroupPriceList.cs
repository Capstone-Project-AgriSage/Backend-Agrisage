using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Pricing.Entities;

// "One Customer Group has one applicable Price List at a time" is enforced by
// Application + database constraints, not by this entity.
public sealed class CustomerGroupPriceList : SoftDeletableEntity
{
    private CustomerGroupPriceList()
    {
    }

    public CustomerGroupPriceList(
        Guid customerGroupId,
        Guid priceListId,
        DateTimeOffset effectiveFrom,
        Guid assignedBy,
        DateTimeOffset? effectiveTo = null)
    {
        Guard.ValidPeriod(effectiveFrom, effectiveTo);

        CustomerGroupId = customerGroupId;
        PriceListId = priceListId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        AssignedBy = assignedBy;
    }

    public Guid CustomerGroupId { get; private set; }

    public Guid PriceListId { get; private set; }

    public PriceList PriceList { get; private set; } = null!;

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public Guid AssignedBy { get; private set; }

    public bool IsCurrent => EffectiveTo is null;

    public void End(DateTimeOffset effectiveTo)
    {
        if (!IsCurrent)
        {
            throw new DomainException("Customer group price list assignment has already ended.");
        }

        Guard.ValidPeriod(EffectiveFrom, effectiveTo);
        EffectiveTo = effectiveTo;
    }
}
