using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Exchanges.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Domain.Features.Exchanges;

public class ExchangeTests
{
    private static (SalesExchange E, AgriSage.Domain.Features.Orders.Entities.Order O) Create()
    {
        var (o, item) = CreateConfirmedOrderWithItem(); o.RecordFulfillment(item.Id, BaseQuantity, StaffId, Now);
        var r = new SalesReturn(o, "RT-TEST", StaffId, Now);
        var e = new SalesExchange(r, Guid.NewGuid(), new string('A', 64), StaffId, "Wrong product");
        e.AddReplacement(item); return (e, o);
    }
    [Theory]
    [InlineData(0, 240000)]
    [InlineData(200000, 40000)]
    [InlineData(240000, 0)]
    [InlineData(300000, -60000)]
    public void Settlement_uses_paid_return_credit_and_keeps_signed_difference(decimal credit, decimal difference)
    {
        var (e, o) = Create(); e.Approve(); e.Receive(); e.FinishInspection(credit);
        Assert.Equal(difference, e.Difference); Assert.Equal(ExchangeStatus.ReadyToComplete, e.Status);
        Assert.Throws<DomainException>(() => e.Complete(o, difference + 1, "Receipt", Now));
        Assert.Throws<DomainException>(() => e.Cancel());
    }
    [Fact]
    public void Goods_cannot_be_released_before_inspection_and_terminal_states_cannot_be_reused()
    {
        var (e, o) = Create(); Assert.Throws<DomainException>(() => e.Receive());
        e.Approve(); Assert.Throws<DomainException>(() => e.Complete(o, 0, "Receipt", Now));
        e.Cancel(); Assert.Throws<DomainException>(() => e.Approve()); Assert.Throws<DomainException>(() => e.Receive());
        var (rejected, _) = Create(); rejected.Reject(); Assert.Throws<DomainException>(() => rejected.Approve());
    }
    [Fact]
    public void Completion_requires_a_distinct_matching_customer_and_value_and_is_terminal()
    {
        var (e, original) = Create(); e.Approve(); e.Receive(); e.FinishInspection(e.ReplacementAmount);
        // The original completed order is never a replacement document.
        Assert.Throws<DomainException>(() => e.Complete(original, 0, "Receipt", Now));
    }
}
