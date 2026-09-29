using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.UnitTests.Domain.Features.Debt;

public class DebtTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly DueDate = new(2026, 10, 1);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly DebtAccount _account = new(Guid.NewGuid(), Guid.NewGuid());

    private DebtEntry CreatePickupEntry(decimal fulfillmentValue, decimal prepayment = 0) =>
        _account.CreateCreditSaleEntry(
            "DE-0001",
            DebtEntrySourceType.Pickup,
            orderId: Guid.NewGuid(),
            sourceStockMovementId: Guid.NewGuid(),
            fulfillmentValue,
            prepayment,
            DueDate,
            StaffId,
            Now).Entry;

    private static PaymentAllocation AllocateDebtPayment(DebtEntry entry, decimal amount)
    {
        var payment = new Payment(Guid.NewGuid(), "PAY-0001", PaymentContext.DebtRepayment, PaymentMethod.Cash, amount, Now);
        payment.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);
        return payment.AllocateToDebtEntry(entry.Id, amount, Now, StaffId);
    }

    [Fact]
    public void Credit_sale_entry_covers_only_the_unpaid_amount_and_is_ledgered()
    {
        // Database design §XVI case C: delivered 20M, prepayment 10M → debt 10M.
        var posting = _account.CreateCreditSaleEntry(
            "DE-0001", DebtEntrySourceType.Pickup, Guid.NewGuid(), Guid.NewGuid(),
            fulfillmentValue: 20_000_000m, prepaymentAppliedAmount: 10_000_000m, DueDate, StaffId, Now);

        Assert.Equal(10_000_000m, posting.Entry.OriginalAmount);
        Assert.Equal(10_000_000m, posting.Entry.OutstandingAmount);
        Assert.Equal(DebtEntryStatus.Open, posting.Entry.Status);
        Assert.Equal(DebtTransactionType.CreditSale, posting.Transaction.TransactionType);
        Assert.Equal(10_000_000m, posting.Transaction.AmountDelta);
        Assert.Equal(10_000_000m, posting.Transaction.BalanceAfter);
        Assert.Equal(10_000_000m, _account.CurrentBalance);
        Assert.Equal(Now, _account.LastTransactionAt);
    }

    [Fact]
    public void No_debt_entry_without_an_unpaid_amount()
    {
        Assert.Throws<DomainException>(() => CreatePickupEntry(20_000_000m, prepayment: 20_000_000m));
        Assert.Throws<DomainException>(() => CreatePickupEntry(20_000_000m, prepayment: 25_000_000m));
        Assert.Equal(0m, _account.CurrentBalance);
    }

    [Fact]
    public void Delivery_entry_requires_its_delivery_and_stock_movement()
    {
        Assert.Throws<DomainException>(() => _account.CreateCreditSaleEntry(
            "DE-0001", DebtEntrySourceType.Delivery, Guid.NewGuid(), Guid.NewGuid(),
            20_000_000m, 0, DueDate, StaffId, Now, deliveryId: null));

        Assert.Throws<DomainException>(() => _account.CreateCreditSaleEntry(
            "DE-0001", DebtEntrySourceType.ManualAdjustment, Guid.NewGuid(), Guid.NewGuid(),
            20_000_000m, 0, DueDate, StaffId, Now));
    }

    [Fact]
    public void Payments_reduce_entry_and_account_through_the_ledger()
    {
        var entry = CreatePickupEntry(25_000_000m);

        var first = _account.ApplyPayment(entry, AllocateDebtPayment(entry, 20_000_000m), Now, StaffId);
        Assert.Equal(DebtEntryStatus.PartiallyPaid, entry.Status);
        Assert.Equal(5_000_000m, entry.OutstandingAmount);
        Assert.Equal(DebtTransactionType.Payment, first.TransactionType);
        Assert.Equal(-20_000_000m, first.AmountDelta);
        Assert.NotNull(first.PaymentAllocationId);

        _account.ApplyPayment(entry, AllocateDebtPayment(entry, 5_000_000m), Now, StaffId);
        Assert.Equal(DebtEntryStatus.Paid, entry.Status);
        Assert.Equal(0m, _account.CurrentBalance);
    }

    [Fact]
    public void Payment_cannot_exceed_outstanding_or_use_a_foreign_allocation()
    {
        var entry = CreatePickupEntry(10_000_000m);
        var otherEntry = CreatePickupEntry(10_000_000m);

        Assert.Throws<DomainException>(() =>
            _account.ApplyPayment(entry, AllocateDebtPayment(entry, 10_000_001m), Now));
        Assert.Throws<DomainException>(() =>
            _account.ApplyPayment(entry, AllocateDebtPayment(otherEntry, 1_000m), Now));

        Assert.Equal(10_000_000m, entry.OutstandingAmount);
        Assert.Equal(20_000_000m, _account.CurrentBalance);
    }

    [Fact]
    public void Disputed_entry_still_accepts_payment_until_the_dispute_is_resolved()
    {
        var entry = CreatePickupEntry(10_000_000m);
        entry.Dispute("Farmer says 2 bags were missing", StaffId);

        _account.ApplyPayment(entry, AllocateDebtPayment(entry, 4_000_000m), Now);
        Assert.Equal(DebtEntryStatus.Disputed, entry.Status);
        Assert.Equal(6_000_000m, entry.OutstandingAmount);

        entry.Keep("Delivery proof confirms full quantity", StaffId);
        Assert.Equal(DebtEntryStatus.PartiallyPaid, entry.Status);
        Assert.Equal(2, entry.Actions.Count);
    }

    [Fact]
    public void Adjust_only_decreases_outstanding_and_is_ledgered()
    {
        var entry = CreatePickupEntry(10_000_000m);
        entry.Dispute("Price disagreement", StaffId);

        var transaction = _account.AdjustEntry(entry, 3_000_000m, "Agreed discount", StaffId, Now);

        Assert.Equal(DebtEntryStatus.Adjusted, entry.Status);
        Assert.Equal(7_000_000m, entry.OutstandingAmount);
        Assert.Equal(DebtTransactionType.AdjustmentOut, transaction.TransactionType);
        Assert.Equal(-3_000_000m, transaction.AmountDelta);
        var action = Assert.Single(entry.Actions, a => a.ActionType == DebtEntryActionType.Adjust);
        Assert.Equal(action.Id, transaction.DebtEntryActionId);
        Assert.Equal(-3_000_000m, action.AdjustmentAmount);
        Assert.Equal(7_000_000m, _account.CurrentBalance);

        Assert.Throws<DomainException>(() => _account.AdjustEntry(entry, 7_000_001m, "Too much", StaffId, Now));
    }

    [Fact]
    public void Cancel_removes_the_remaining_receivable()
    {
        var entry = CreatePickupEntry(10_000_000m);
        _account.ApplyPayment(entry, AllocateDebtPayment(entry, 4_000_000m), Now);

        var transaction = _account.CancelEntry(entry, "Goods returned before use", StaffId, Now);

        Assert.Equal(DebtEntryStatus.Cancelled, entry.Status);
        Assert.Equal(0m, entry.OutstandingAmount);
        Assert.Equal(-6_000_000m, transaction.AmountDelta);
        Assert.Equal(0m, _account.CurrentBalance);
        Assert.Throws<DomainException>(() => entry.Dispute("Too late", StaffId));
        Assert.Throws<DomainException>(() => entry.ChangeDueDate(DueDate.AddDays(10), "Too late", StaffId));
    }

    [Fact]
    public void Manual_adjustment_entry_increases_receivable_with_adjustment_in()
    {
        var posting = _account.CreateManualAdjustmentEntry("DE-0002", 2_000_000m, DueDate, StaffId, Now);

        Assert.Equal(DebtEntrySourceType.ManualAdjustment, posting.Entry.SourceType);
        Assert.Equal(DebtTransactionType.AdjustmentIn, posting.Transaction.TransactionType);
        Assert.Equal(2_000_000m, _account.CurrentBalance);
    }

    [Fact]
    public void Due_date_change_is_recorded_as_an_action()
    {
        var entry = CreatePickupEntry(10_000_000m);

        var action = entry.ChangeDueDate(DueDate.AddDays(15), "Harvest delayed", StaffId);

        Assert.Equal(DueDate.AddDays(15), entry.DueDate);
        Assert.Equal(DueDate, action.OldDueDate);
        Assert.Equal(DueDate.AddDays(15), action.NewDueDate);
    }

    [Fact]
    public void Entry_of_another_account_is_rejected()
    {
        var otherAccount = new DebtAccount(Guid.NewGuid(), Guid.NewGuid());
        var foreignEntry = otherAccount.CreateManualAdjustmentEntry("DE-0003", 1_000m, DueDate, StaffId, Now).Entry;

        Assert.Throws<DomainException>(() => _account.CancelEntry(foreignEntry, "Wrong account", StaffId, Now));
    }

    [Fact]
    public void Ledger_records_cannot_be_deleted()
    {
        var posting = _account.CreateManualAdjustmentEntry("DE-0002", 2_000_000m, DueDate, StaffId, Now);

        Assert.Throws<DomainException>(() => posting.Transaction.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => posting.Entry.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => _account.MarkDeleted(StaffId, Now));
    }
}
