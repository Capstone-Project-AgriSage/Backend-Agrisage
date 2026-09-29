using AgriSage.Domain.Common;
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
        DateOnly? newDueDate = null)
    {
        DebtEntryId = debtEntryId;
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

    public string Reason { get; private set; } = null!;

    public Guid CreatedBy { get; private set; }
}
