using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Enums;

namespace AgriSage.Domain.Features.Credit.Entities;

// Credit exposure reserved for a confirmed credit Order not yet fully fulfilled.
// Invariant: consumed + released <= reserved. Status is derived from amounts (database design §35.3).
// "Required <= Available" is checked through FarmerCreditProfile.EnsureCanReserve in the same transaction.
public sealed class CreditReservation : SoftDeletableEntity
{
    private CreditReservation()
    {
    }

    public CreditReservation(
        Guid storeId,
        Guid farmerCreditProfileId,
        Guid orderId,
        decimal amountReserved,
        Guid reservedBy,
        DateTimeOffset reservedAt)
    {
        StoreId = storeId;
        FarmerCreditProfileId = farmerCreditProfileId;
        OrderId = orderId;
        AmountReserved = Guard.NonNegativeMoney(amountReserved);
        ReservedBy = reservedBy;
        ReservedAt = reservedAt;
        Status = CreditReservationStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid FarmerCreditProfileId { get; private set; }

    public Guid OrderId { get; private set; }

    public decimal AmountReserved { get; private set; }

    public decimal AmountConsumed { get; private set; }

    public decimal AmountReleased { get; private set; }

    public CreditReservationStatus Status { get; private set; }

    public DateTimeOffset ReservedAt { get; private set; }

    public Guid ReservedBy { get; private set; }

    // Most recent release action.
    public DateTimeOffset? ReleasedAt { get; private set; }

    public Guid? ReleasedBy { get; private set; }

    public string? ReleaseReason { get; private set; }

    public decimal RemainingAmount => AmountReserved - AmountConsumed - AmountReleased;

    // Unpaid fulfilled value that becomes a Debt Entry.
    public void Consume(decimal amount)
    {
        EnsureOpen();
        EnsureWithinRemaining(amount);
        AmountConsumed += amount;
        RefreshStatus();
    }

    // E.g. an additional confirmed upfront payment reduces the unused reservation.
    public void Release(decimal amount, Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();
        EnsureWithinRemaining(amount);
        AmountReleased += amount;
        RecordRelease(releasedBy, releasedAt, reason);
        RefreshStatus();
    }

    public void ReleaseRemaining(Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();

        if (RemainingAmount == 0)
        {
            throw new DomainException("Credit reservation has no remaining amount to release.");
        }

        AmountReleased += RemainingAmount;
        RecordRelease(releasedBy, releasedAt, reason);
        RefreshStatus();
    }

    // Order cancelled before anything was consumed.
    public void Cancel(Guid releasedBy, DateTimeOffset releasedAt, string? reason = null)
    {
        EnsureOpen();

        if (AmountConsumed > 0)
        {
            throw new DomainException("A partially consumed credit reservation cannot be cancelled; release the remainder instead.");
        }

        AmountReleased += RemainingAmount;
        RecordRelease(releasedBy, releasedAt, reason);
        Status = CreditReservationStatus.Cancelled;
    }

    // Reservations are released or cancelled, never deleted.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Credit reservations cannot be deleted; release or cancel them.");

    private void EnsureWithinRemaining(decimal amount)
    {
        Guard.PositiveMoney(amount);

        if (amount > RemainingAmount)
        {
            throw new DomainException($"Amount {amount} exceeds the remaining reserved credit {RemainingAmount}.");
        }
    }

    private void RecordRelease(Guid releasedBy, DateTimeOffset releasedAt, string? reason)
    {
        ReleasedBy = releasedBy;
        ReleasedAt = releasedAt;
        ReleaseReason = reason;
    }

    private void EnsureOpen()
    {
        if (Status is not (CreditReservationStatus.Active or CreditReservationStatus.PartiallyConsumed))
        {
            throw new DomainException($"Credit reservation is {Status} and can no longer change.");
        }
    }

    private void RefreshStatus() =>
        Status = RemainingAmount > 0
            ? AmountConsumed > 0 ? CreditReservationStatus.PartiallyConsumed : CreditReservationStatus.Active
            : AmountConsumed > 0 ? CreditReservationStatus.Consumed : CreditReservationStatus.Released;
}
