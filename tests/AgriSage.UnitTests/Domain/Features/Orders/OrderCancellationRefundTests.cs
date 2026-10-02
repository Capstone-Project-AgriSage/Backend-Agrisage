using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Domain.Features.Orders;

// Refunds of order prepayment left unused by a cancellation (database design §35.18).
public class OrderCancellationRefundTests
{
    private static readonly Guid PaymentId = Guid.NewGuid();

    [Fact]
    public void A_cancelled_order_records_a_pending_refund_for_one_payment()
    {
        var (order, _) = CreateConfirmedOrderWithItem();
        order.Cancel(StaffId, Now, "Out of stock");

        var refund = order.RequestCancellationRefund("RF-20260901-0001", PaymentId, RefundMethod.Cash, 240_000m, StaffId, Now);

        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Equal(order.Id, refund.OrderId);
        Assert.Null(refund.SalesReturnId);
        Assert.Equal(PaymentId, refund.OriginalPaymentId);
        Assert.Equal(order.StoreId, refund.StoreId);
        Assert.Equal(240_000m, refund.Amount);
        Assert.Single(order.CancellationRefunds);
    }

    [Fact]
    public void A_partially_cancelled_order_can_refund_its_unused_prepayment()
    {
        var (order, item) = CreateConfirmedOrderWithItem();
        order.RecordFulfillment(item.Id, 6, StaffId, Now);
        order.CancelItemRemaining(item.Id, StaffId, Now);
        Assert.Equal(OrderStatus.PartiallyCancelled, order.Status);

        var refund = order.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.BankTransfer, 120_000m, StaffId, Now);

        Assert.Equal(RefundStatus.Pending, refund.Status);
    }

    [Fact]
    public void Orders_that_are_not_cancelled_cannot_be_refunded_this_way()
    {
        var (pending, _) = CreateOrderWithItem();
        var (confirmed, _) = CreateConfirmedOrderWithItem();

        Assert.Throws<DomainException>(() => pending.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.Cash, 1m, StaffId, Now));
        Assert.Throws<DomainException>(() => confirmed.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.Cash, 1m, StaffId, Now));
    }

    [Fact]
    public void A_cancellation_refund_needs_a_payment_and_positive_money()
    {
        var (order, _) = CreateConfirmedOrderWithItem();
        order.Cancel(StaffId, Now);

        Assert.Throws<DomainException>(() => order.RequestCancellationRefund("RF-1", Guid.Empty, RefundMethod.Cash, 1m, StaffId, Now));
        Assert.Throws<DomainException>(() => order.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.Cash, 0m, StaffId, Now));
        Assert.Throws<DomainException>(() => order.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.Cash, 1.001m, StaffId, Now));
        Assert.Throws<DomainException>(() => order.RequestCancellationRefund(" ", PaymentId, RefundMethod.Cash, 1m, StaffId, Now));
    }

    [Fact]
    public void Refunds_are_completed_failed_or_cancelled_once_through_the_order()
    {
        var (order, _) = CreateConfirmedOrderWithItem();
        order.Cancel(StaffId, Now);
        var completed = order.RequestCancellationRefund("RF-1", PaymentId, RefundMethod.Cash, 100_000m, StaffId, Now);
        var failed = order.RequestCancellationRefund("RF-2", PaymentId, RefundMethod.BankTransfer, 50_000m, StaffId, Now);
        var cancelled = order.RequestCancellationRefund("RF-3", PaymentId, RefundMethod.Cash, 50_000m, StaffId, Now);

        order.CompleteCancellationRefund(completed.Id, StaffId, Now, "CASH-DESK-1");
        order.FailCancellationRefund(failed.Id);
        order.CancelCancellationRefund(cancelled.Id, StaffId, Now, "Customer kept the money as a new order");

        Assert.Equal(RefundStatus.Completed, completed.Status);
        Assert.Equal("CASH-DESK-1", completed.ExternalReference);
        Assert.Equal(StaffId, completed.CompletedBy);
        Assert.Equal(RefundStatus.Failed, failed.Status);
        Assert.Equal(RefundStatus.Cancelled, cancelled.Status);
        Assert.True(completed.CountsTowardRefundTotal);
        Assert.False(failed.CountsTowardRefundTotal);
        Assert.False(cancelled.CountsTowardRefundTotal);

        Assert.Throws<DomainException>(() => order.CompleteCancellationRefund(completed.Id, StaffId, Now));
        Assert.Throws<DomainException>(() => order.FailCancellationRefund(cancelled.Id));
        Assert.Throws<DomainException>(() => order.CompleteCancellationRefund(Guid.NewGuid(), StaffId, Now));
    }
}
