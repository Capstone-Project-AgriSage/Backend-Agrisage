using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Orders.Enums;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task F3.4 registers the real IFulfillmentFinancialPosting (then delete this file and its registration).
// FULL_PAYMENT orders create no debt, but their prepayment is consumed by what is handed over (oldest allocation first),
// so allocations, refunds on cancellation and returns see the right consumed amount before F3.4 exists. A CREDIT order here
// means the temporary guard was bypassed, which must not go unnoticed.
public sealed class TemporaryFulfillmentFinancialPosting(IOrderPrepaymentLedger ledger) : IFulfillmentFinancialPosting
{
    public async Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken)
    {
        if (context.Order.SettlementType == SettlementType.Credit)
        {
            throw new InvalidOperationException("A CREDIT order was fulfilled before the real debt posting (task F3.4) exists.");
        }

        await ledger.ConsumeAsync(context.Order.Id, context.Lines.Sum(l => l.FulfilledValue), cancellationToken);
    }
}
