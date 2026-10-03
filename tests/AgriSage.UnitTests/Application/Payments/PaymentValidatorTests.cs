using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Payments.Enums;

namespace AgriSage.UnitTests.Application.Payments;

// Request validation of the payment API (FLOW_1 §5), before any database access.
public class PaymentValidatorTests
{
    private static bool Valid(CashPaymentRequest request) => new CashPaymentRequestValidator().Validate(request).IsValid;

    private static CashPaymentRequest OrderPayment(decimal amount = 100_000m) => new("ORDER_PAYMENT", amount, Guid.NewGuid());

    private static CashPaymentRequest Repayment(decimal amount = 100_000m, params DebtAllocationInput[] allocations) =>
        new("DEBT_REPAYMENT", amount, FarmerProfileId: Guid.NewGuid(), DebtAllocations: allocations.Length == 0 ? null : allocations);

    [Fact]
    public void An_order_payment_needs_an_order_and_a_positive_money_amount()
    {
        Assert.True(Valid(OrderPayment()));
        Assert.True(Valid(OrderPayment(0.5m)));
        Assert.True(Valid(OrderPayment() with { PaymentContext = "order_payment" }));
        Assert.False(Valid(OrderPayment(0m)));
        Assert.False(Valid(OrderPayment(-1m)));
        Assert.False(Valid(OrderPayment(10.005m)));
        Assert.False(Valid(OrderPayment() with { OrderId = null }));
        Assert.False(Valid(OrderPayment() with { OrderId = Guid.Empty }));
        Assert.False(Valid(OrderPayment() with { PaymentContext = "REFUND" }));
        Assert.False(Valid(OrderPayment() with { Note = new string('n', 1001) }));
        Assert.False(Valid(OrderPayment() with { DebtAllocations = [new DebtAllocationInput(Guid.NewGuid(), 1m)] }));
    }

    [Fact]
    public void A_debt_repayment_needs_a_customer_and_allocations_that_add_up()
    {
        var entry = Guid.NewGuid();

        Assert.True(Valid(Repayment()));
        Assert.True(Valid(Repayment(300m, new DebtAllocationInput(entry, 100m), new DebtAllocationInput(Guid.NewGuid(), 200m))));
        Assert.False(Valid(Repayment() with { FarmerProfileId = null }));
        Assert.False(Valid(Repayment() with { OrderId = Guid.NewGuid() }));
        Assert.False(Valid(Repayment(300m, new DebtAllocationInput(entry, 100m), new DebtAllocationInput(Guid.NewGuid(), 100m))));
        Assert.False(Valid(Repayment(200m, new DebtAllocationInput(entry, 100m), new DebtAllocationInput(entry, 100m))));
        Assert.False(Valid(Repayment(100m, new DebtAllocationInput(Guid.Empty, 100m))));
        Assert.False(Valid(Repayment(100m, new DebtAllocationInput(entry, 0m), new DebtAllocationInput(Guid.NewGuid(), 100m))));
        Assert.False(Valid(Repayment(1m, new DebtAllocationInput(entry, 1.005m))));
    }

    [Fact]
    public void List_filters_accept_only_known_values_and_ordered_dates()
    {
        var validator = new PaymentListRequestValidator();
        var day = new DateOnly(2026, 10, 3);

        Assert.True(validator.Validate(new PaymentListRequest
        {
            PaymentContext = "debt_repayment", PaymentMethod = "PAYOS", Status = "partially_refunded", FromDate = day, ToDate = day, Search = "PM-"
        }).IsValid);
        Assert.False(validator.Validate(new PaymentListRequest { PaymentContext = "REFUND" }).IsValid);
        Assert.False(validator.Validate(new PaymentListRequest { PaymentMethod = "CARD" }).IsValid);
        Assert.False(validator.Validate(new PaymentListRequest { Status = "LOST" }).IsValid);
        Assert.False(validator.Validate(new PaymentListRequest { FromDate = day, ToDate = day.AddDays(-1) }).IsValid);
        Assert.False(validator.Validate(new PaymentListRequest { PageSize = 0 }).IsValid);

        var mine = new MyPaymentListRequestValidator();
        Assert.True(mine.Validate(new MyPaymentListRequest { Status = "PAID" }).IsValid);
        Assert.False(mine.Validate(new MyPaymentListRequest { Status = "LOST" }).IsValid);
        Assert.False(new CancelPaymentRequestValidator().Validate(new CancelPaymentRequest(new string('r', 501))).IsValid);
    }

    [Fact]
    public void Payment_method_and_confirmation_source_use_the_stored_texts()
    {
        Assert.Equal("CASH", PaymentText.Format(PaymentMethod.Cash));
        Assert.Equal("PAYOS", PaymentText.Format(PaymentMethod.PayOs));
        Assert.Equal("STAFF", PaymentText.Format(PaymentConfirmationSource.Staff));
        Assert.Equal("PAYOS_WEBHOOK", PaymentText.Format(PaymentConfirmationSource.PayOsWebhook));
        Assert.True(PaymentText.TryParseMethod("payos", out var method) && method == PaymentMethod.PayOs);
        Assert.True(PaymentText.TryParseMethod(" cash ", out method) && method == PaymentMethod.Cash);
        Assert.False(PaymentText.TryParseMethod("PAY_OS", out _));
    }
}
