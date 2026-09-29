using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Customers.Entities;

// Assignment history row. "One current Customer Group per Farmer at a time" is enforced by the
// assignment use case + partial unique index (effective_to IS NULL), not by this entity.
public sealed class CustomerGroupAssignment : SoftDeletableEntity
{
    private CustomerGroupAssignment()
    {
    }

    public CustomerGroupAssignment(
        Guid farmerProfileId,
        Guid customerGroupId,
        DateTimeOffset effectiveFrom,
        Guid assignedBy,
        string? reason = null)
    {
        FarmerProfileId = farmerProfileId;
        CustomerGroupId = customerGroupId;
        EffectiveFrom = effectiveFrom;
        AssignedBy = assignedBy;
        Reason = reason;
    }

    public Guid FarmerProfileId { get; private set; }

    public Guid CustomerGroupId { get; private set; }

    public CustomerGroup CustomerGroup { get; private set; } = null!;

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public Guid AssignedBy { get; private set; }

    public string? Reason { get; private set; }

    public bool IsCurrent => EffectiveTo is null;

    public void End(DateTimeOffset effectiveTo)
    {
        if (!IsCurrent)
        {
            throw new DomainException("Customer group assignment has already ended.");
        }

        Guard.ValidPeriod(EffectiveFrom, effectiveTo);
        EffectiveTo = effectiveTo;
    }
}
