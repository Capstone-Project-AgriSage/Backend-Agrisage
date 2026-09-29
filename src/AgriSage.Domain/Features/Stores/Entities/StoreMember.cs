using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Stores.Enums;

namespace AgriSage.Domain.Features.Stores.Entities;

// Staff membership (Store Owner / Sales Staff / Delivery Staff). UNIQUE(store_id, user_id):
// a returning member reuses this record via Activate().
public sealed class StoreMember : SoftDeletableEntity
{
    private StoreMember()
    {
    }

    public StoreMember(Guid storeId, Guid userId, string? employeeCode = null, DateOnly? joinedAt = null)
    {
        StoreId = storeId;
        UserId = userId;
        EmployeeCode = employeeCode;
        JoinedAt = joinedAt;
        Status = StoreMemberStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid UserId { get; private set; }

    public User User { get; private set; } = null!;

    public string? EmployeeCode { get; private set; }

    public DateOnly? JoinedAt { get; private set; }

    public DateOnly? LeftAt { get; private set; }

    public StoreMemberStatus Status { get; private set; }

    // AI Human Review permission; which roles may hold it is checked by Application.
    public bool CanReviewAi { get; private set; }

    public void UpdateEmployment(string? employeeCode, DateOnly? joinedAt)
    {
        if (joinedAt is not null && LeftAt is not null && LeftAt < joinedAt)
        {
            throw new DomainException("Join date cannot be later than the leave date.");
        }

        EmployeeCode = employeeCode;
        JoinedAt = joinedAt;
    }

    public void GrantAiReview() => CanReviewAi = true;

    public void RevokeAiReview() => CanReviewAi = false;

    public void Leave(DateOnly leftAt)
    {
        if (Status == StoreMemberStatus.Left)
        {
            throw new DomainException("Store member has already left.");
        }

        if (JoinedAt is not null && leftAt < JoinedAt)
        {
            throw new DomainException("Leave date cannot be earlier than the join date.");
        }

        Status = StoreMemberStatus.Left;
        LeftAt = leftAt;
    }

    public void Activate()
    {
        Status = StoreMemberStatus.Active;
        LeftAt = null;
    }

    public void Deactivate()
    {
        if (Status == StoreMemberStatus.Left)
        {
            throw new DomainException("A member who has left must be reactivated before changing status.");
        }

        Status = StoreMemberStatus.Inactive;
    }
}
