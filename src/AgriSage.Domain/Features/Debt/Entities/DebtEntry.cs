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
        Guid? sourceStockMovementId = null,
        Guid? originCropSeasonId = null,
        string? dueBasis = null)
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
        OriginalDueDate = dueDate;
        OriginCropSeasonId = originCropSeasonId;
        CollectionCropSeasonId = originCropSeasonId;
        DueBasis = string.IsNullOrWhiteSpace(dueBasis) ? null : dueBasis;
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

    // D9: the season the debt arose in (fixed at creation, never changes) and the season it is collected in (moves on
    // rollover). Both are null when no season applied and the credit-tier term was used.
    public Guid? OriginCropSeasonId { get; private set; }

    public Guid? CollectionCropSeasonId { get; private set; }

    // The due date the debt was created with; extensions are measured from it and reports show "debt carried over".
    public DateOnly OriginalDueDate { get; private set; }

    public int RolloverCount { get; private set; }

    // D17: the values the due date was computed from (season, harvest end, grace, cap, or the tier term), as jsonb, so a
    // later change of the season or the credit tier never silently moves an existing debt. Null for legacy entries.
    public string? DueBasis { get; private set; }

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

    public DebtEntryAction ChangeDueDate(DateOnly newDueDate, string reason, Guid createdBy, DebtReasonCode? reasonCode = null)
    {
        EnsureNotClosed();

        var oldDueDate = DueDate;
        DueDate = newDueDate;

        return AddAction(
            DebtEntryActionType.ChangeDueDate,
            reason,
            createdBy,
            oldDueDate: oldDueDate,
            newDueDate: newDueDate,
            reasonCode: reasonCode);
    }

    // CS-07 / D5: extend an unpaid entry to a later season. Only the collection season and the due date move; the amount,
    // the status, the origin season and the original due date stay. Who may do it (CS-08) and the total extension limit are
    // checked by the application against StoreDebtPolicy, because they depend on the actor's role.
    public DebtEntryAction Rollover(Guid newCropSeasonId, DateOnly newDueDate, DebtReasonCode reasonCode, string note, Guid createdBy)
    {
        EnsureNotClosed();
        if (Status == DebtEntryStatus.Disputed)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' is disputed; resolve the dispute before extending it.");
        }

        if (OutstandingAmount == 0)
        {
            throw new DomainException($"Debt entry '{EntryNumber}' has nothing left to extend.");
        }

        Guard.NotEmpty(newCropSeasonId);
        if (newCropSeasonId == CollectionCropSeasonId)
        {
            throw new DomainException("A rollover moves the debt to another season.");
        }

        if (newDueDate <= DueDate)
        {
            throw new DomainException("A rollover must move the due date later.");
        }

        if (!DebtReasonCodes.AllowsRollover(reasonCode))
        {
            throw new DomainException($"Reason code {reasonCode} cannot justify a rollover.");
        }

        var oldSeason = CollectionCropSeasonId;
        var oldDueDate = DueDate;
        var action = AddAction(DebtEntryActionType.Rollover, note, createdBy, oldDueDate: oldDueDate, newDueDate: newDueDate,
            oldCropSeasonId: oldSeason, newCropSeasonId: newCropSeasonId, reasonCode: reasonCode);
        CollectionCropSeasonId = newCropSeasonId;
        DueDate = newDueDate;
        RolloverCount++;
        return action;
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
    internal DebtEntryAction Adjust(decimal decreaseAmount, string reason, Guid createdBy, DebtReasonCode? reasonCode = null)
    {
        EnsureNotClosed();
        EnsureReductionCode(reasonCode);
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
            resultingOutstanding: OutstandingAmount,
            reasonCode: reasonCode);
    }

    // CANCEL removes the whole remaining receivable.
    internal DebtEntryAction Cancel(string reason, Guid createdBy, DebtReasonCode? reasonCode = null)
    {
        EnsureNotClosed();
        EnsureReductionCode(reasonCode);

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
            resultingOutstanding: 0,
            reasonCode: reasonCode);
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
        DateOnly? newDueDate = null,
        Guid? oldCropSeasonId = null,
        Guid? newCropSeasonId = null,
        DebtReasonCode? reasonCode = null)
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
            newDueDate,
            oldCropSeasonId,
            newCropSeasonId,
            reasonCode);

        _actions.Add(action);

        return action;
    }

    // D7: a missing code is still accepted here (legacy callers); the application makes it mandatory for new actions.
    private static void EnsureReductionCode(DebtReasonCode? reasonCode)
    {
        if (reasonCode is { } code && !DebtReasonCodes.AllowsReduction(code))
        {
            throw new DomainException($"Reason code {code} cannot justify reducing a debt.");
        }
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
