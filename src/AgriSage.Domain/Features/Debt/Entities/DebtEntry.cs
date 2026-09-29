using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// One concrete receivable obligation; aggregate root for its Actions. Created only by DebtAccount.
// Amount-changing operations (payment, ADJUST, CANCEL) are internal so they always go through DebtAccount,
// which posts the matching Debt Transaction (database design §35.6).
public sealed class DebtEntry : SoftDeletableEntity
{
    private readonly List<DebtEntryAction> _actions = [];

    private DebtEntry()
    {
    }

    internal DebtEntry(
        Guid debtAccountId,
        string entryNumber,
        DebtEntrySourceType sourceType,
        decimal fulfillmentValue,
        decimal prepaymentAppliedAmount,
        DateOnly dueDate,
        Guid createdBy,
        Guid? orderId = null,
        Guid? deliveryId = null,
        Guid? deliveryAttemptId = null,
        Guid? sourceStockMovementId = null)
    {
        Guard.NonNegativeMoney(fulfillmentValue);
        Guard.NonNegativeMoney(prepaymentAppliedAmount);

        if (prepaymentAppliedAmount > fulfillmentValue)
        {
            throw new DomainException("Applied prepayment cannot exceed the fulfillment value.");
        }

        var originalAmount = fulfillmentValue - prepaymentAppliedAmount;

        // Debt is created only for an unpaid amount.
        if (originalAmount <= 0)
        {
            throw new DomainException("A debt entry requires an unpaid amount greater than zero.");
        }

        EnsureSourceReferences(sourceType, orderId, deliveryId, sourceStockMovementId);

        DebtAccountId = debtAccountId;
        EntryNumber = Guard.NotNullOrWhiteSpace(entryNumber);
        SourceType = sourceType;
        OrderId = orderId;
        DeliveryId = deliveryId;
        DeliveryAttemptId = deliveryAttemptId;
        SourceStockMovementId = sourceStockMovementId;
        FulfillmentValue = fulfillmentValue;
        PrepaymentAppliedAmount = prepaymentAppliedAmount;
        OriginalAmount = originalAmount;
        OutstandingAmount = originalAmount;
        DueDate = dueDate;
        CreatedBy = createdBy;
        Status = DebtEntryStatus.Open;
    }

    public Guid DebtAccountId { get; private set; }

    public string EntryNumber { get; private set; } = null!;

    public Guid? OrderId { get; private set; }

    public Guid? DeliveryId { get; private set; }

    public Guid? DeliveryAttemptId { get; private set; }

    public Guid? SourceStockMovementId { get; private set; }

    public DebtEntrySourceType SourceType { get; private set; }

    public decimal FulfillmentValue { get; private set; }

    public decimal PrepaymentAppliedAmount { get; private set; }

    public decimal OriginalAmount { get; private set; }

    public decimal OutstandingAmount { get; private set; }

    public DateOnly DueDate { get; private set; }

    public DebtEntryStatus Status { get; private set; }

    public Guid CreatedBy { get; private set; }

    public IReadOnlyCollection<DebtEntryAction> Actions => _actions.AsReadOnly();

    // DISPUTE does not change the receivable and does not block payments.
    public DebtEntryAction Dispute(string reason, Guid createdBy)
    {
        EnsureNotClosed();

        if (Status == DebtEntryStatus.Disputed)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' is already disputed.");
        }

        Status = DebtEntryStatus.Disputed;

        return AddAction(DebtEntryActionType.Dispute, reason, createdBy);
    }

    // KEEP resolves a dispute with the amount unchanged.
    public DebtEntryAction Keep(string reason, Guid createdBy)
    {
        if (Status != DebtEntryStatus.Disputed)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' is not disputed.");
        }

        RefreshStatus();

        return AddAction(DebtEntryActionType.Keep, reason, createdBy);
    }

    public DebtEntryAction ChangeDueDate(DateOnly newDueDate, string reason, Guid createdBy)
    {
        EnsureNotClosed();

        var oldDueDate = DueDate;
        DueDate = newDueDate;

        return AddAction(
            DebtEntryActionType.ChangeDueDate,
            reason,
            createdBy,
            oldDueDate: oldDueDate,
            newDueDate: newDueDate);
    }

    internal void ApplyPayment(decimal amount)
    {
        EnsureNotClosed();
        EnsureWithinOutstanding(amount);

        OutstandingAmount -= amount;

        // A dispute stays open until KEEP / ADJUST / CANCEL, even while payments arrive.
        if (Status != DebtEntryStatus.Disputed)
        {
            RefreshStatus();
        }
    }

    // ADJUST only decreases the outstanding amount; returns the action the Debt Transaction references.
    internal DebtEntryAction Adjust(decimal decreaseAmount, string reason, Guid createdBy)
    {
        EnsureNotClosed();
        EnsureWithinOutstanding(decreaseAmount);

        var previous = OutstandingAmount;
        OutstandingAmount -= decreaseAmount;
        Status = OutstandingAmount > 0 ? DebtEntryStatus.Adjusted : DebtEntryStatus.Paid;

        return AddAction(
            DebtEntryActionType.Adjust,
            reason,
            createdBy,
            previousOutstanding: previous,
            adjustment: -decreaseAmount,
            resultingOutstanding: OutstandingAmount);
    }

    // CANCEL removes the whole remaining receivable.
    internal DebtEntryAction Cancel(string reason, Guid createdBy)
    {
        EnsureNotClosed();

        if (OutstandingAmount == 0)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' has no outstanding amount to cancel.");
        }

        var previous = OutstandingAmount;
        OutstandingAmount = 0;
        Status = DebtEntryStatus.Cancelled;

        return AddAction(
            DebtEntryActionType.Cancel,
            reason,
            createdBy,
            previousOutstanding: previous,
            adjustment: -previous,
            resultingOutstanding: 0);
    }

    // Debt entries are never deleted; they are paid, adjusted or cancelled.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Debt entries cannot be deleted; adjust or cancel them.");

    private static void EnsureSourceReferences(
        DebtEntrySourceType sourceType,
        Guid? orderId,
        Guid? deliveryId,
        Guid? sourceStockMovementId)
    {
        var valid = sourceType switch
        {
            DebtEntrySourceType.Delivery => orderId is not null && deliveryId is not null && sourceStockMovementId is not null,
            DebtEntrySourceType.Pickup => orderId is not null && sourceStockMovementId is not null,
            _ => true
        };

        if (!valid)
        {
            throw new DomainException($"A {sourceType} debt entry is missing its required source references.");
        }
    }

    private DebtEntryAction AddAction(
        DebtEntryActionType actionType,
        string reason,
        Guid createdBy,
        decimal? previousOutstanding = null,
        decimal? adjustment = null,
        decimal? resultingOutstanding = null,
        DateOnly? oldDueDate = null,
        DateOnly? newDueDate = null)
    {
        var action = new DebtEntryAction(
            Id,
            actionType,
            reason,
            createdBy,
            previousOutstanding,
            adjustment,
            resultingOutstanding,
            oldDueDate,
            newDueDate);

        _actions.Add(action);

        return action;
    }

    private void EnsureWithinOutstanding(decimal amount)
    {
        Guard.PositiveMoney(amount);

        if (amount > OutstandingAmount)
        {
            throw new DomainException($"Amount {amount} exceeds the outstanding amount {OutstandingAmount}.");
        }
    }

    private void EnsureNotClosed()
    {
        if (Status is DebtEntryStatus.Paid or DebtEntryStatus.Cancelled)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' is {Status} and can no longer change.");
        }
    }

    private void RefreshStatus() =>
        Status = OutstandingAmount == 0 ? DebtEntryStatus.Paid
            : OutstandingAmount < OriginalAmount ? DebtEntryStatus.PartiallyPaid
            : DebtEntryStatus.Open;
}
