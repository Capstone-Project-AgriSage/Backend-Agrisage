using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Application.Payments;

// The PaymentAllocator step (F1.3) with fake cross-flow interfaces.
public class PaymentAllocatorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeCredit : ICreditReservationAdjuster
    {
        public List<(Guid OrderId, decimal Amount)> Calls { get; } = [];

        public Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken)
        {
            Calls.Add((orderId, paidAmount));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDebt(decimal applied) : IDebtRepaymentPosting
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
            Payment payment, IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId, CancellationToken cancellationToken)
        {
            Calls++;
            if (applied > 0)
            {
                payment.AllocateToDebtEntry(Guid.NewGuid(), applied, Now, actorId);
            }

            return Task.FromResult<IReadOnlyList<DebtAllocationResult>>([]);
        }
    }

    private static Payment Paid(PaymentContext context, decimal amount, Guid? orderId = null)
    {
        var payment = new Payment(Guid.NewGuid(), "PM-1", context, PaymentMethod.Cash, amount, Now, null, StaffId, orderId: orderId);
        payment.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);
        return payment;
    }

    private static Order Confirmed(SettlementType settlement)
    {
        var (order, _) = CreateOrderWithItem(settlementType: settlement);
        order.Confirm(StaffId, Now, settlement == SettlementType.Credit ? 30 : null);
        return order;
    }

    [Fact]
    public async Task An_order_payment_is_allocated_whole_to_its_order()
    {
        var credit = new FakeCredit();
        var allocator = new PaymentAllocator(new FixedClock(), credit, new FakeDebt(0m));
        var (order, _) = CreateOrderWithItem();
        var payment = Paid(PaymentContext.OrderPayment, 120_000m, order.Id);

        await allocator.AllocateAsync(payment, order, null, StaffId, Token);

        var allocation = Assert.Single(payment.Allocations);
        Assert.Equal((order.Id, 120_000m, 0m), (allocation.OrderId, allocation.AllocatedAmount, payment.UnallocatedAmount));
        Assert.Empty(credit.Calls);
    }

    [Fact]
    public async Task Only_a_confirmed_credit_order_releases_credit_reservation()
    {
        var credit = new FakeCredit();
        var allocator = new PaymentAllocator(new FixedClock(), credit, new FakeDebt(0m));
        var (pendingCredit, _) = CreateOrderWithItem(settlementType: SettlementType.Credit);
        var confirmedCredit = Confirmed(SettlementType.Credit);
        var confirmedCash = Confirmed(SettlementType.FullPayment);

        await allocator.AllocateAsync(Paid(PaymentContext.OrderPayment, 10m, pendingCredit.Id), pendingCredit, null, StaffId, Token);
        await allocator.AllocateAsync(Paid(PaymentContext.OrderPayment, 20m, confirmedCash.Id), confirmedCash, null, StaffId, Token);
        Assert.Empty(credit.Calls);

        await allocator.AllocateAsync(Paid(PaymentContext.OrderPayment, 30m, confirmedCredit.Id), confirmedCredit, null, StaffId, Token);
        Assert.Equal((confirmedCredit.Id, 30m), Assert.Single(credit.Calls));
    }

    [Fact]
    public async Task A_payment_must_be_paid_and_belong_to_the_order()
    {
        var allocator = new PaymentAllocator(new FixedClock(), new FakeCredit(), new FakeDebt(0m));
        var (order, _) = CreateOrderWithItem();
        var pending = new Payment(Guid.NewGuid(), "PM-2", PaymentContext.OrderPayment, PaymentMethod.Cash, 10m, Now, null, StaffId, orderId: order.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => allocator.AllocateAsync(pending, order, null, StaffId, Token));
        await Assert.ThrowsAsync<DomainException>(() =>
            allocator.AllocateAsync(Paid(PaymentContext.OrderPayment, 10m, Guid.NewGuid()), order, null, StaffId, Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            allocator.AllocateAsync(Paid(PaymentContext.OrderPayment, 10m, order.Id), null, null, StaffId, Token));
    }

    [Fact]
    public async Task A_debt_repayment_goes_to_the_debt_posting_and_must_be_applied_whole()
    {
        var full = new FakeDebt(100m);
        await new PaymentAllocator(new FixedClock(), new FakeCredit(), full)
            .AllocateAsync(Paid(PaymentContext.DebtRepayment, 100m), null, null, StaffId, Token);
        Assert.Equal(1, full.Calls);

        var partial = new PaymentAllocator(new FixedClock(), new FakeCredit(), new FakeDebt(60m));
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            partial.AllocateAsync(Paid(PaymentContext.DebtRepayment, 100m), null, null, StaffId, Token));
        Assert.Contains("left over", refused.Message);
    }
}
