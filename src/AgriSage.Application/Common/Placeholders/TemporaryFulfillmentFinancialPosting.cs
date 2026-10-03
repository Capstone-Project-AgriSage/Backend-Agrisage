using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Orders.Enums;

namespace AgriSage.Application.Common.Placeholders;

// TEMPORARY until task B5 registers the real IFulfillmentFinancialPosting (then delete this file and its registration).
// Posts nothing: only FULL_PAYMENT orders can be confirmed meanwhile (TemporaryOrderSettlementGuard), and they create
// no debt. A CREDIT order here means the temporary guard was bypassed, which must not go unnoticed.
public sealed class TemporaryFulfillmentFinancialPosting : IFulfillmentFinancialPosting
{
    public Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken) =>
        context.Order.SettlementType == SettlementType.Credit
            ? throw new InvalidOperationException("A CREDIT order was fulfilled before the real debt posting (task B5) exists.")
            : Task.CompletedTask;
}
