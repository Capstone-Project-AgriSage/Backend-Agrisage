using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.UnitTests.Domain.Features.Payments;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();

    private static Payment CreatePayment(
        PaymentContext context = PaymentContext.OrderPayment,
        PaymentMethod method = PaymentMethod.Cash,
        decimal amount = 50_000_000m) =>
        new(
            Guid.NewGuid(), "PAY-0001", context, method, amount, Now, payerFarmerProfileId: Guid.NewGuid(),
            orderId: context == PaymentContext.OrderPayment ? OrderId : null);

    private static Payment CreatePaidCashPayment(PaymentContext context = PaymentContext.OrderPayment)
    {
        var payment = CreatePayment(context);
        payment.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);
        return payment;
    }

    [Fact]
    public void Amount_must_be_positive_money()
    {
        Assert.Throws<DomainException>(() => CreatePayment(amount: 0));
        Assert.Throws<DomainException>(() => CreatePayment(amount: 10.001m));
    }

    [Fact]
    public void Cash_is_confirmed_by_staff_and_payos_by_webhook()
    {
        var cash = CreatePayment();
        Assert.Throws<DomainException>(() => cash.MarkPaid(PaymentConfirmationSource.PayOsWebhook, Now));
        Assert.Throws<DomainException>(() => cash.MarkPaid(PaymentConfirmationSource.Staff, Now));
        cash.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);
        Assert.Equal(PaymentStatus.Paid, cash.Status);

        var payOs = CreatePayment(method: PaymentMethod.PayOs);
        payOs.SetProviderLink("PAYOS", 123456, "link-1", "https://pay.example/checkout");
        Assert.Throws<DomainException>(() => payOs.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId));
        payOs.MarkPaid(PaymentConfirmationSource.PayOsWebhook, Now, providerTransactionId: "txn-1");
        Assert.Equal("txn-1", payOs.ProviderTransactionId);
        Assert.Throws<DomainException>(() => payOs.MarkPaid(PaymentConfirmationSource.PayOsWebhook, Now));
    }

    [Fact]
    public void Provider_link_is_only_for_payos_payments()
    {
        var cash = CreatePayment();

        Assert.Throws<DomainException>(() => cash.SetProviderLink("PAYOS", 1, "link", "https://pay.example"));
    }

    [Fact]
    public void An_order_payment_names_its_order_and_a_debt_repayment_has_none()
    {
        Assert.Throws<DomainException>(() =>
            new Payment(Guid.NewGuid(), "PAY-1", PaymentContext.OrderPayment, PaymentMethod.Cash, 1m, Now));
        Assert.Throws<DomainException>(() =>
            new Payment(Guid.NewGuid(), "PAY-1", PaymentContext.OrderPayment, PaymentMethod.Cash, 1m, Now, orderId: Guid.Empty));
        Assert.Throws<DomainException>(() =>
            new Payment(Guid.NewGuid(), "PAY-1", PaymentContext.DebtRepayment, PaymentMethod.Cash, 1m, Now, orderId: OrderId));

        Assert.Equal(OrderId, CreatePayment().OrderId);
        Assert.Null(CreatePayment(PaymentContext.DebtRepayment).OrderId);
    }

    [Fact]
    public void An_order_payment_is_allocated_only_to_its_own_order()
    {
        var payment = CreatePaidCashPayment();

        Assert.Throws<DomainException>(() => payment.AllocateToOrder(Guid.NewGuid(), 10_000m, Now));
        Assert.Equal(OrderId, payment.AllocateToOrder(OrderId, 10_000m, Now).OrderId);
    }

    [Fact]
    public void Allocation_requires_a_paid_payment()
    {
        var payment = CreatePayment();

        Assert.Throws<DomainException>(() => payment.AllocateToOrder(OrderId, 10_000m, Now));
    }

    [Fact]
    public void Allocation_type_is_bound_to_payment_context()
    {
        var orderPayment = CreatePaidCashPayment(PaymentContext.OrderPayment);
        var debtPayment = CreatePaidCashPayment(PaymentContext.DebtRepayment);

        Assert.Throws<DomainException>(() => orderPayment.AllocateToDebtEntry(Guid.NewGuid(), 10_000m, Now));
        Assert.Throws<DomainException>(() => debtPayment.AllocateToOrder(OrderId, 10_000m, Now));

        var toOrder = orderPayment.AllocateToOrder(OrderId, 10_000m, Now, StaffId);
        var toDebt = debtPayment.AllocateToDebtEntry(Guid.NewGuid(), 10_000m, Now, StaffId);
        Assert.NotNull(toOrder.OrderId);
        Assert.Null(toOrder.DebtEntryId);
        Assert.NotNull(toDebt.DebtEntryId);
        Assert.Null(toDebt.OrderId);
    }

    [Fact]
    public void Active_allocations_cannot_exceed_the_payment_amount()
    {
        var payment = CreatePaidCashPayment(PaymentContext.DebtRepayment);
        payment.AllocateToDebtEntry(Guid.NewGuid(), 10_000_000m, Now);
        payment.AllocateToDebtEntry(Guid.NewGuid(), 20_000_000m, Now);

        Assert.Throws<DomainException>(() => payment.AllocateToDebtEntry(Guid.NewGuid(), 20_000_001m, Now));
        Assert.Equal(20_000_000m, payment.UnallocatedAmount);
    }

    [Fact]
    public void Prepayment_consumption_cannot_exceed_the_order_allocation()
    {
        var payment = CreatePaidCashPayment();
        var allocation = payment.AllocateToOrder(OrderId, 10_000_000m, Now);

        payment.ConsumePrepayment(allocation.Id, 6_000_000m);

        Assert.Equal(4_000_000m, allocation.AvailablePrepayment);
        Assert.Throws<DomainException>(() => payment.ConsumePrepayment(allocation.Id, 4_000_001m));
    }

    [Fact]
    public void Order_allocation_with_consumed_prepayment_cannot_be_reversed()
    {
        var payment = CreatePaidCashPayment();
        var consumed = payment.AllocateToOrder(OrderId, 10_000_000m, Now);
        var unused = payment.AllocateToOrder(OrderId, 10_000_000m, Now);
        payment.ConsumePrepayment(consumed.Id, 1_000m);

        Assert.Throws<DomainException>(() => payment.ReverseAllocation(consumed.Id, StaffId, Now));

        payment.ReverseAllocation(unused.Id, StaffId, Now, "Allocated to the wrong order");
        Assert.Equal(PaymentAllocationStatus.Reversed, unused.Status);
        Assert.Equal(40_000_000m, payment.UnallocatedAmount);
    }

    [Fact]
    public void Paid_payment_cannot_be_deleted_and_allocation_cannot_be_deleted_directly()
    {
        var payment = CreatePaidCashPayment();
        var allocation = payment.AllocateToOrder(OrderId, 10_000m, Now);

        Assert.Throws<DomainException>(() => payment.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => allocation.MarkDeleted(StaffId, Now));
    }
}
