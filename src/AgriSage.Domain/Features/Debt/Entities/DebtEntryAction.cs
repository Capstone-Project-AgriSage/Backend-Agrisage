using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// Dispute or staff decision on a Debt Entry. Created only by DebtEntry.
// adjustment_amount follows the ledger sign convention (negative = receivable decreases).
public sealed class DebtEntryAction : SoftDeletableChildEntity
{
    private DebtEntryAction()
    {
    }

    internal DebtEntryAction(
        Guid debtEntryId,
        DebtEntryActionType actionType,
        string reason,
        Guid createdBy,
        decimal? previousOutstandingAmount = null,
        decimal? adjustmentAmount = null,
        decimal? resultingOutstandingAmount = null,
        DateOnly? oldDueDate = null,
        DateOnly? newDueDate = null,
        Guid? oldCropSeasonId = null,
        Guid? newCropSeasonId = null,
        DebtReasonCode? reasonCode = null)
    {
        if (reasonCode is { } code && !Enum.IsDefined(code))
        {
            throw new DomainException("Unknown debt reason code.");
        }

        DebtEntryId = debtEntryId;
        OldCropSeasonId = oldCropSeasonId;
        NewCropSeasonId = newCropSeasonId;
        ReasonCode = reasonCode;
        ActionType = actionType;
        Reason = Guard.NotNullOrWhiteSpace(reason);
        CreatedBy = createdBy;
        PreviousOutstandingAmount = previousOutstandingAmount;
        AdjustmentAmount = adjustmentAmount;
        ResultingOutstandingAmount = resultingOutstandingAmount;
        OldDueDate = oldDueDate;
        NewDueDate = newDueDate;
    }

    public Guid DebtEntryId { get; private set; }

    public DebtEntryActionType ActionType { get; private set; }

    public decimal? PreviousOutstandingAmount { get; private set; }

    public decimal? AdjustmentAmount { get; private set; }

    public decimal? ResultingOutstandingAmount { get; private set; }

    public DateOnly? OldDueDate { get; private set; }

    public DateOnly? NewDueDate { get; private set; }

    // Collection season before and after a ROLLOVER (D9).
    public Guid? OldCropSeasonId { get; private set; }

    public Guid? NewCropSeasonId { get; private set; }

    // CS-11 reason code; null for actions recorded before the policy (D7) and for actions that take none.
    public DebtReasonCode? ReasonCode { get; private set; }

    // Free-text note typed by the user, always required.
    public string Reason { get; private set; } = null!;

    public Guid CreatedBy { get; private set; }
}
