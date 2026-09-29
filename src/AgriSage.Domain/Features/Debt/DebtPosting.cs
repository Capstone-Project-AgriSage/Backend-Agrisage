using AgriSage.Domain.Features.Debt.Entities;

namespace AgriSage.Domain.Features.Debt;

// A new Debt Entry together with the ledger transaction that created it.
public sealed record DebtPosting(DebtEntry Entry, DebtTransaction Transaction);
