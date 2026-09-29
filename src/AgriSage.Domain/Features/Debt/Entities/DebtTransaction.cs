using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// Immutable Accounts Receivable ledger row. Created only by DebtAccount, which keeps
// current_balance and balance_after consistent; never edited or deleted.
// Sign convention: positive amount_delta = receivable increases, negative = decreases.
public sealed class DebtTransaction : SoftDeletableChildEntity
{
    private DebtTransaction()
    {
    }

    internal DebtTransaction(
        Guid debtAccountId,
        Guid? debtEntryId,
        DebtTransactionType transactionType,
        decimal amountDelta,
        decimal balanceAfter,
        DateTimeOffset occurredAt,
        Guid? createdBy,
        Guid? paymentAllocationId = null,
        Guid? debtEntryActionId = null,
        string? note = null)
    {
        DebtAccountId = debtAccountId;
        DebtEntryId = debtEntryId;
        TransactionType = transactionType;
        AmountDelta = amountDelta;
        BalanceAfter = balanceAfter;
        OccurredAt = occurredAt;
        CreatedBy = createdBy;
        PaymentAllocationId = paymentAllocationId;
        DebtEntryActionId = debtEntryActionId;
        Note = note;
        Status = DebtTransactionStatus.Posted;
    }

    public Guid DebtAccountId { get; private set; }

    public Guid? DebtEntryId { get; private set; }

    public Guid? PaymentAllocationId { get; private set; }

    // FK to sales_returns (entity introduced in a later task).
    public Guid? SalesReturnId { get; private set; }

    public Guid? DebtEntryActionId { get; private set; }

    public DebtTransactionType TransactionType { get; private set; }

    public decimal AmountDelta { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DebtTransactionStatus Status { get; private set; }

    public Guid? ReversalOfTransactionId { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public string? Note { get; private set; }
}
