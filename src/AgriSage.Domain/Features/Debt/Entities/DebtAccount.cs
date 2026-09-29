using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// Accounts Receivable account of one registered Farmer per Store. The only place that creates
// Debt Transactions, so every receivable change is ledgered and current_balance stays consistent.
// Invariants: current_balance >= 0, balance_after >= 0, amount_delta <> 0.
public sealed class DebtAccount : SoftDeletableEntity, IHasConcurrencyVersion
{
    private DebtAccount()
    {
    }

    public DebtAccount(Guid storeId, Guid farmerProfileId)
    {
        StoreId = storeId;
        FarmerProfileId = farmerProfileId;
        Status = DebtAccountStatus.Active;
    }

    public Guid StoreId { get; private set; }

    public Guid FarmerProfileId { get; private set; }

    public FarmerProfile FarmerProfile { get; private set; } = null!;

    // Transactionally maintained cache of the ledger; the ledger remains debt_transactions.
    public decimal CurrentBalance { get; private set; }

    public DebtAccountStatus Status { get; private set; }

    public DateTimeOffset? LastTransactionAt { get; private set; }

    public long Version { get; private set; }

    public void ChangeStatus(DebtAccountStatus status) => Status = status;

    // Successful credit fulfillment: Debt Entry for the unpaid amount + CREDIT_SALE transaction.
    public DebtPosting CreateCreditSaleEntry(
        string entryNumber,
        DebtEntrySourceType sourceType,
        Guid orderId,
        Guid sourceStockMovementId,
        decimal fulfillmentValue,
        decimal prepaymentAppliedAmount,
        DateOnly dueDate,
        Guid createdBy,
        DateTimeOffset occurredAt,
        Guid? deliveryId = null,
        Guid? deliveryAttemptId = null)
    {
        if (sourceType is not (DebtEntrySourceType.Delivery or DebtEntrySourceType.Pickup))
        {
            throw new DomainException("A credit sale entry comes from a DELIVERY or PICKUP fulfillment.");
        }

        var entry = new DebtEntry(
            Id,
            entryNumber,
            sourceType,
            fulfillmentValue,
            prepaymentAppliedAmount,
            dueDate,
            createdBy,
            orderId,
            deliveryId,
            deliveryAttemptId,
            sourceStockMovementId);

        var transaction = Post(DebtTransactionType.CreditSale, entry.OriginalAmount, entry.Id, occurredAt, createdBy);

        return new DebtPosting(entry, transaction);
    }

    // Increasing a Farmer's receivable is done with a new MANUAL_ADJUSTMENT entry (ADJUSTMENT_IN).
    public DebtPosting CreateManualAdjustmentEntry(
        string entryNumber,
        decimal amount,
        DateOnly dueDate,
        Guid createdBy,
        DateTimeOffset occurredAt,
        Guid? orderId = null,
        string? note = null)
    {
        var entry = new DebtEntry(
            Id,
            entryNumber,
            DebtEntrySourceType.ManualAdjustment,
            fulfillmentValue: amount,
            prepaymentAppliedAmount: 0,
            dueDate,
            createdBy,
            orderId);

        var transaction = Post(
            DebtTransactionType.AdjustmentIn,
            entry.OriginalAmount,
            entry.Id,
            occurredAt,
            createdBy,
            note: note);

        return new DebtPosting(entry, transaction);
    }

    // One PAYMENT transaction per DEBT allocation (Payment → Payment Allocation → Debt Transaction).
    public DebtTransaction ApplyPayment(
        DebtEntry entry,
        PaymentAllocation allocation,
        DateTimeOffset occurredAt,
        Guid? createdBy = null)
    {
        EnsureOwns(entry);

        if (allocation.AllocationType != PaymentAllocationType.Debt || !allocation.IsActive || allocation.IsDeleted)
        {
            throw new DomainException("Only an active DEBT payment allocation can be applied to a debt entry.");
        }

        if (allocation.DebtEntryId != entry.Id)
        {
            throw new DomainException("The payment allocation targets a different debt entry.");
        }

        EnsureCanPost(-allocation.AllocatedAmount);
        entry.ApplyPayment(allocation.AllocatedAmount);

        return Post(
            DebtTransactionType.Payment,
            -allocation.AllocatedAmount,
            entry.Id,
            occurredAt,
            createdBy,
            paymentAllocationId: allocation.Id);
    }

    public DebtTransaction AdjustEntry(
        DebtEntry entry,
        decimal decreaseAmount,
        string reason,
        Guid createdBy,
        DateTimeOffset occurredAt)
    {
        EnsureOwns(entry);
        EnsureCanPost(-decreaseAmount);

        var action = entry.Adjust(decreaseAmount, reason, createdBy);

        return Post(
            DebtTransactionType.AdjustmentOut,
            -decreaseAmount,
            entry.Id,
            occurredAt,
            createdBy,
            debtEntryActionId: action.Id);
    }

    public DebtTransaction CancelEntry(DebtEntry entry, string reason, Guid createdBy, DateTimeOffset occurredAt)
    {
        EnsureOwns(entry);
        EnsureCanPost(-entry.OutstandingAmount);

        var action = entry.Cancel(reason, createdBy);

        return Post(
            DebtTransactionType.AdjustmentOut,
            action.AdjustmentAmount!.Value,
            entry.Id,
            occurredAt,
            createdBy,
            debtEntryActionId: action.Id);
    }

    // Accounts are kept for history; never deleted.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Debt accounts cannot be deleted.");

    // Checked before any Debt Entry is changed, so a rejected posting leaves no partial state.
    private void EnsureCanPost(decimal amountDelta)
    {
        if (Guard.Money(amountDelta) == 0)
        {
            throw new DomainException("A debt transaction must change the balance.");
        }

        if (CurrentBalance + amountDelta < 0)
        {
            throw new DomainException("The debt account balance cannot become negative.");
        }
    }

    private void EnsureOwns(DebtEntry entry)
    {
        if (entry.DebtAccountId != Id)
        {
            throw new DomainException($"Debt entry '{entry.EntryNumber}' belongs to another debt account.");
        }
    }

    private DebtTransaction Post(
        DebtTransactionType transactionType,
        decimal amountDelta,
        Guid? debtEntryId,
        DateTimeOffset occurredAt,
        Guid? createdBy,
        Guid? paymentAllocationId = null,
        Guid? debtEntryActionId = null,
        string? note = null)
    {
        EnsureCanPost(amountDelta);

        var balanceAfter = CurrentBalance + amountDelta;

        CurrentBalance = balanceAfter;
        LastTransactionAt = occurredAt;

        return new DebtTransaction(
            Id,
            debtEntryId,
            transactionType,
            amountDelta,
            balanceAfter,
            occurredAt,
            createdBy,
            paymentAllocationId,
            debtEntryActionId,
            note);
    }
}
