using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.UnitTests.Domain.Features.Debt;

public sealed class CreditDebtCollectionTests
{
    private static readonly Guid Store = Guid.NewGuid(), Farmer = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Due = new(2026, 11, 4);
    private static (DebtAccount Account, DebtEntry Entry) Debt(decimal value = 10_000_000)
    {
        var a = new DebtAccount(Store, Farmer);
        return (a, a.CreateCreditSaleEntry("DE-1", DebtEntrySourceType.Pickup, Guid.NewGuid(), Guid.NewGuid(), value, 0, Due, Actor, Now).Entry);
    }
    private static Payment Paid(decimal amount, PaymentMethod method = PaymentMethod.Cash)
    {
        var p = new Payment(Store, "PM-1", PaymentContext.DebtRepayment, method, amount, Now, Farmer, Actor);
        p.MarkPaid(PaymentConfirmationSource.Staff, Now, Actor);
        return p;
    }

    [Fact]
    public void Three_partial_payments_preserve_original_and_settle_exactly()
    {
        var (account, entry) = Debt();
        var ledger = new List<DebtTransaction>();
        foreach (var amount in new[] { 3_000_000m, 5_000_000m, 2_000_000m })
        {
            var payment = Paid(amount);
            ledger.Add(account.ApplyPayment(entry, payment.AllocateToDebtEntry(entry.Id, amount, Now, Actor), Now, Actor));
        }
        Assert.Equal(10_000_000m, entry.OriginalAmount);
        Assert.Equal(0, entry.OutstandingAmount); Assert.Equal(0, account.CurrentBalance);
        Assert.Equal(DebtEntryStatus.Paid, entry.Status);
        Assert.Equal(new[] { 7_000_000m, 2_000_000m, 0m }, ledger.Select(t => t.BalanceAfter));
        Assert.Equal(3, ledger.Select(t => t.PaymentAllocationId).Distinct().Count());
    }
    [Fact]
    public void Return_after_partial_payment_posts_return_and_keeps_original_debt()
    {
        var (account, entry) = Debt();
        var p = Paid(2_000_000);
        account.ApplyPayment(entry, p.AllocateToDebtEntry(entry.Id, p.Amount, Now), Now);
        var returnId = Guid.NewGuid();
        var t = account.ApplyReturn(entry, 3_000_000, returnId, Now, Actor);
        Assert.Equal(DebtTransactionType.Return, t.TransactionType); Assert.Equal(returnId, t.SalesReturnId);
        Assert.Equal(-3_000_000m, t.AmountDelta); Assert.Equal(5_000_000m, t.BalanceAfter);
        Assert.Equal(10_000_000m, entry.OriginalAmount); Assert.Equal(5_000_000m, entry.OutstandingAmount);
        account.ApplyReturn(entry, 5_000_000, Guid.NewGuid(), Now, Actor);
        Assert.Equal(DebtEntryStatus.Paid, entry.Status); Assert.Equal(0, account.CurrentBalance);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10000001)]
    public void Invalid_return_does_not_change_balances(decimal amount)
    {
        var (account, entry) = Debt();
        Assert.Throws<DomainException>(() => account.ApplyReturn(entry, amount, Guid.NewGuid(), Now, Actor));
        Assert.Equal(10_000_000m, account.CurrentBalance); Assert.Equal(10_000_000m, entry.OutstandingAmount);
    }
    [Fact]
    public void Return_requires_matching_account_and_source()
    {
        var (account, entry) = Debt();
        Assert.Throws<DomainException>(() => account.ApplyReturn(entry, 1, Guid.Empty, Now, Actor));
        Assert.Throws<DomainException>(() => new DebtAccount(Store, Guid.NewGuid()).ApplyReturn(entry, 1, Guid.NewGuid(), Now, Actor));
    }
    [Fact]
    public void Bank_transfer_is_pending_until_staff_confirmation_and_cannot_be_confirmed_twice()
    {
        var p = new Payment(Store, "PM-1", PaymentContext.DebtRepayment, PaymentMethod.BankTransfer, 100, Now, Farmer);
        Assert.Equal(PaymentStatus.Pending, p.Status);
        Assert.Throws<DomainException>(() => p.AllocateToDebtEntry(Guid.NewGuid(), 100, Now));
        Assert.Throws<DomainException>(() => p.MarkPaid(PaymentConfirmationSource.PayOsWebhook, Now));
        p.MarkPaid(PaymentConfirmationSource.Staff, Now, Actor);
        Assert.Equal(PaymentStatus.Paid, p.Status);
        Assert.Throws<DomainException>(() => p.MarkPaid(PaymentConfirmationSource.Staff, Now, Actor));
    }
    [Fact]
    public void Rejected_bank_receipt_cannot_be_allocated_or_confirmed()
    {
        var p = new Payment(Store, "PM-1", PaymentContext.DebtRepayment, PaymentMethod.BankTransfer, 100, Now, Farmer);
        p.MarkFailed(Now);
        Assert.Throws<DomainException>(() => p.AllocateToDebtEntry(Guid.NewGuid(), 100, Now));
        Assert.Throws<DomainException>(() => p.MarkPaid(PaymentConfirmationSource.Staff, Now, Actor));
    }
    [Fact]
    public void Disabling_credit_preserves_debt_and_blocks_new_reservations()
    {
        var (account, entry) = Debt();
        var profile = new FarmerCreditProfile(Store, Farmer, 20_000_000, Actor, Now);
        profile.ChangeStatus(FarmerCreditProfileStatus.Suspended);
        Assert.Throws<DomainException>(() => profile.EnsureCanReserve(1, account.CurrentBalance, 0));
        Assert.Equal(10_000_000m, entry.OutstandingAmount);
        account.ApplyPayment(entry, Paid(1).AllocateToDebtEntry(entry.Id, 1, Now), Now);
        Assert.Equal(9_999_999m, account.CurrentBalance);
    }
}
