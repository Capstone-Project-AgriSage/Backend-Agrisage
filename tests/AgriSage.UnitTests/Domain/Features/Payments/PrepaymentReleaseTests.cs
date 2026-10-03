using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.UnitTests.Domain.Features.Payments;

// Giving back the unconsumed part of an order prepayment (database design §35.21).
public class PrepaymentReleaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static (Payment Payment, PaymentAllocation Allocation) PaidAllocation(decimal amount = 500_000m)
    {
        var orderId = Guid.NewGuid();
        var payment = new Payment(Guid.NewGuid(), "PM-1", PaymentContext.OrderPayment, PaymentMethod.Cash, amount, Now, null, StaffId, orderId: orderId);
        payment.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);

        return (payment, payment.AllocateToOrder(orderId, amount, Now, StaffId));
    }

    [Fact]
    public void Part_of_the_unconsumed_prepayment_goes_back_and_the_allocation_shrinks_and_stays_active()
    {
        var (payment, allocation) = PaidAllocation();
        payment.ConsumePrepayment(allocation.Id, 200_000m);

        payment.ReleaseUnconsumedPrepayment(allocation.Id, 100_000m, StaffId, Now, "Khách không lấy nữa");

        Assert.Equal((400_000m, 200_000m, PaymentAllocationStatus.Active), (allocation.AllocatedAmount, allocation.PrepaymentConsumedAmount, allocation.Status));
        Assert.Equal(200_000m, allocation.AvailablePrepayment);
        Assert.Equal(100_000m, payment.UnallocatedAmount);
        Assert.Null(allocation.ReversedAt);
    }

    [Fact]
    public void Everything_unconsumed_with_nothing_consumed_reverses_the_allocation()
    {
        var (payment, allocation) = PaidAllocation();

        payment.ReleaseUnconsumedPrepayment(allocation.Id, 500_000m, StaffId, Now, "Huỷ đơn");

        Assert.Equal((PaymentAllocationStatus.Reversed, "Huỷ đơn", StaffId), (allocation.Status, allocation.ReversalReason, allocation.ReversedBy));
        Assert.Equal(500_000m, payment.UnallocatedAmount);
        Assert.Equal(0m, allocation.AvailablePrepayment);
    }

    [Fact]
    public void Consumed_prepayment_cannot_be_released_and_neither_can_more_than_is_unconsumed()
    {
        var (payment, allocation) = PaidAllocation();
        payment.ConsumePrepayment(allocation.Id, 200_000m);

        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 300_000.01m, StaffId, Now));
        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 0m, StaffId, Now));
        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 10.005m, StaffId, Now));

        // Releasing all that is unconsumed leaves exactly the consumed part allocated.
        payment.ReleaseUnconsumedPrepayment(allocation.Id, 300_000m, StaffId, Now);
        Assert.Equal((200_000m, 200_000m, PaymentAllocationStatus.Active), (allocation.AllocatedAmount, allocation.PrepaymentConsumedAmount, allocation.Status));
        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 1m, StaffId, Now));
    }

    [Fact]
    public void A_reversed_allocation_and_an_unknown_one_cannot_be_released()
    {
        var (payment, allocation) = PaidAllocation();
        payment.ReleaseUnconsumedPrepayment(allocation.Id, 500_000m, StaffId, Now);

        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 1m, StaffId, Now));
        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(Guid.NewGuid(), 1m, StaffId, Now));
    }

    [Fact]
    public void A_debt_allocation_holds_no_prepayment_to_release()
    {
        var payment = new Payment(Guid.NewGuid(), "PM-2", PaymentContext.DebtRepayment, PaymentMethod.Cash, 100m, Now, null, StaffId);
        payment.MarkPaid(PaymentConfirmationSource.Staff, Now, StaffId);
        var allocation = payment.AllocateToDebtEntry(Guid.NewGuid(), 100m, Now, StaffId);

        Assert.Throws<DomainException>(() => payment.ReleaseUnconsumedPrepayment(allocation.Id, 10m, StaffId, Now));
    }
}
