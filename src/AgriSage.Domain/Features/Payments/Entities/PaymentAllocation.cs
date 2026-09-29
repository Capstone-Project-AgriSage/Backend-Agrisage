using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.Domain.Features.Payments.Entities;

// Share of a confirmed Payment applied to exactly one target: an Order prepayment pool or one Debt Entry.
// Changed only through Payment. Invariant: 0 <= prepayment consumed <= allocated (DEBT: always 0).
public sealed class PaymentAllocation : SoftDeletableChildEntity
{
    private PaymentAllocation()
    {
    }

    internal PaymentAllocation(
        Guid paymentId,
        PaymentAllocationType allocationType,
        Guid targetId,
        decimal allocatedAmount,
        DateTimeOffset allocatedAt,
        Guid? allocatedBy)
    {
        PaymentId = paymentId;
        AllocationType = allocationType;
        OrderId = allocationType == PaymentAllocationType.Order ? targetId : null;
        DebtEntryId = allocationType == PaymentAllocationType.Debt ? targetId : null;
        AllocatedAmount = Guard.PositiveMoney(allocatedAmount);
        Status = PaymentAllocationStatus.Active;
        AllocatedAt = allocatedAt;
        AllocatedBy = allocatedBy;
    }

    public Guid PaymentId { get; private set; }

    public PaymentAllocationType AllocationType { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? DebtEntryId { get; private set; }

    public decimal AllocatedAmount { get; private set; }

    public decimal PrepaymentConsumedAmount { get; private set; }

    public PaymentAllocationStatus Status { get; private set; }

    public DateTimeOffset AllocatedAt { get; private set; }

    public Guid? AllocatedBy { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public Guid? ReversedBy { get; private set; }

    public string? ReversalReason { get; private set; }

    public bool IsActive => Status == PaymentAllocationStatus.Active;

    // Unapplied Order prepayment still available to fulfillment.
    public decimal AvailablePrepayment =>
        AllocationType == PaymentAllocationType.Order && IsActive ? AllocatedAmount - PrepaymentConsumedAmount : 0;

    internal void ConsumePrepayment(decimal amount)
    {
        if (AllocationType != PaymentAllocationType.Order || !IsActive)
        {
            throw new DomainException("Only an active ORDER allocation holds prepayment.");
        }

        Guard.PositiveMoney(amount);

        if (amount > AvailablePrepayment)
        {
            throw new DomainException($"Cannot consume {amount}; only {AvailablePrepayment} prepayment is available.");
        }

        PrepaymentConsumedAmount += amount;
    }

    internal void Reverse(Guid reversedBy, DateTimeOffset reversedAt, string? reason)
    {
        if (!IsActive)
        {
            throw new DomainException("Payment allocation is already reversed.");
        }

        if (PrepaymentConsumedAmount > 0)
        {
            throw new DomainException("An ORDER allocation whose prepayment was consumed cannot be reversed.");
        }

        Status = PaymentAllocationStatus.Reversed;
        ReversedBy = reversedBy;
        ReversedAt = reversedAt;
        ReversalReason = reason;
    }
}
