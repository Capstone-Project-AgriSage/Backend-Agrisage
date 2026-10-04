using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Application.CrossModule;

// Behavior of the temporary cross-module implementations until their owner tasks deliver the real ones.
public class TemporaryImplementationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Full_payment_orders_pass_the_settlement_guard_and_credit_orders_are_refused()
    {
        var guard = new TemporaryOrderSettlementGuard();

        var result = await guard.EnsureCanConfirmAsync(CreateOrder(), StaffId, Token);

        Assert.Null(result.CreditTermDays);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            guard.EnsureCanConfirmAsync(CreateOrder(settlementType: SettlementType.Credit), StaffId, Token));
        await guard.ReleaseAsync(CreateOrder(), StaffId, "cancelled", Token);
    }

    private sealed class RecordingLedger : IOrderPrepaymentLedger
    {
        public List<(Guid OrderId, decimal Max)> Consumed { get; } = [];

        public Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(0m);

        public Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult(0m);

        public Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken)
        {
            Consumed.Add((orderId, maxAmount));
            return Task.FromResult(maxAmount);
        }
    }

    [Fact]
    public async Task Fulfillment_posting_consumes_prepayment_for_full_payment_and_fails_loudly_for_credit()
    {
        var ledger = new RecordingLedger();
        var posting = new TemporaryFulfillmentFinancialPosting(ledger);
        var order = CreateOrder();
        FulfillmentPostingContext Context(Order target, params FulfilledLine[] lines) => new(
            target, lines, FulfillmentSource.Pickup, null, null, Guid.NewGuid(), StaffId, Now);

        await posting.PostAsync(Context(order, new FulfilledLine(Guid.NewGuid(), 6, 55_000m), new FulfilledLine(Guid.NewGuid(), 1, 10_000m)), Token);

        // What was handed over is the value of the prepayment that is used up.
        Assert.Equal((order.Id, 65_000m), Assert.Single(ledger.Consumed));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            posting.PostAsync(Context(CreateOrder(settlementType: SettlementType.Credit)), Token));
        Assert.Single(ledger.Consumed);
    }

    [Fact]
    public async Task Credit_and_debts_that_do_not_exist_yet_change_nothing_or_are_refused()
    {
        var payment = new Payment(Guid.NewGuid(), "PM-1", PaymentContext.DebtRepayment, PaymentMethod.Cash, 1m, Now);

        await new TemporaryCreditReservationAdjuster().OnOrderPrepaymentAsync(Guid.NewGuid(), 1m, StaffId, Token);
        Assert.Equal(0m, await new TemporaryDebtReturnPosting().ApplyReturnAsync(Guid.NewGuid(), Guid.NewGuid(), 5m, StaffId, null, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => new TemporaryDebtRepaymentPosting().ApplyAsync(payment, null, StaffId, Token));
    }

    [Fact]
    public void Document_prefixes_are_distinct_and_follow_the_contract()
    {
        string[] prefixes =
        [
            DocumentNumbers.GoodsReceipt, DocumentNumbers.StockMovement, DocumentNumbers.Order, DocumentNumbers.Delivery,
            DocumentNumbers.Payment, DocumentNumbers.Stocktake, DocumentNumbers.SalesReturn, DocumentNumbers.Refund,
            DocumentNumbers.DebtEntry
        ];

        Assert.Equal(["GR", "SM", "OD", "DL", "PM", "ST", "RT", "RF", "DE"], prefixes);
        Assert.Equal(prefixes.Length, prefixes.Distinct().Count());
        Assert.Equal("OD-20261003-0012", DocumentNumbers.Format(DocumentNumbers.Order, new DateOnly(2026, 10, 3), 12));
    }
}
