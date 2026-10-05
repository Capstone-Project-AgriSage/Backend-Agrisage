using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Enums;

namespace AgriSage.Tests.TestDoubles;

// Test-only posting for inventory/order tests: consume prepayment, and refuse credit outside this isolation.
public sealed class StubFulfillmentFinancialPosting(IOrderPrepaymentLedger ledger) : IFulfillmentFinancialPosting
{
    public async Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken)
    {
        if (context.Order.SettlementType == SettlementType.Credit)
        {
            throw new InvalidOperationException("This isolated fulfillment test does not post credit debt.");
        }

        await ledger.ConsumeAsync(context.Order.Id, context.Lines.Sum(l => l.FulfilledValue), cancellationToken);
    }
}
