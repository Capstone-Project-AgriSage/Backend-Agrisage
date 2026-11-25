using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt;

// Whether a promise to pay made during a manual reminder was kept (F3.23). Only a label: nothing acts on it.
public static class DebtFollowUpPolicy
{
    // The amount the customer promised, or, when no amount was given, what was overdue when the reminder was recorded
    // (the whole balance when nothing was overdue yet).
    public static decimal PromiseTarget(decimal? promisedAmount, decimal outstandingAmount, decimal overdueAmount) =>
        promisedAmount ?? (overdueAmount > 0 ? overdueAmount : outstandingAmount);

    // null without a promise. Payments are the posted PAYMENT ledger lines since the contact: those up to the end of the
    // promised day (Vietnam) decide KEPT; the later ones only make it LATE. A debt that ends without that much being paid
    // (a return, a reduction, a write-off) is CLEARED, not kept. Otherwise BROKEN once the promised day has passed, else
    // WAITING.
    public static DebtPromiseStatus? PromiseStatus(DateOnly? promisedDate, decimal? promisedAmount, decimal outstandingAmount,
        decimal overdueAmount, decimal paidByPromisedDate, decimal paidSinceContact, decimal currentBalance, DateOnly today)
    {
        if (promisedDate is not { } date) return null;
        var target = PromiseTarget(promisedAmount, outstandingAmount, overdueAmount);
        if (paidByPromisedDate >= target) return DebtPromiseStatus.Kept;
        if (paidSinceContact >= target) return DebtPromiseStatus.Late;
        if (currentBalance <= 0) return DebtPromiseStatus.Cleared;
        return date < today ? DebtPromiseStatus.Broken : DebtPromiseStatus.Waiting;
    }
}
