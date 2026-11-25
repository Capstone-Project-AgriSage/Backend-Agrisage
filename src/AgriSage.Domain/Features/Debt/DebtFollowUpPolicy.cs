using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt;

// Whether a promise to pay made during a manual reminder was kept (F3.23). Only a label: nothing acts on it.
public static class DebtFollowUpPolicy
{
    // The amount the customer promised, or, when no amount was given, what was overdue when the reminder was recorded
    // (the whole balance when nothing was overdue yet).
    public static decimal PromiseTarget(decimal? promisedAmount, decimal outstandingAmount, decimal overdueAmount) =>
        promisedAmount ?? (overdueAmount > 0 ? overdueAmount : outstandingAmount);

    // null without a promise. KEPT when the posted payments since the contact reach the target or nothing is owed any more;
    // BROKEN once the promised date (Vietnam day) has passed without that; WAITING until then.
    public static DebtPromiseStatus? PromiseStatus(DateOnly? promisedDate, decimal? promisedAmount, decimal outstandingAmount,
        decimal overdueAmount, decimal paidSinceContact, decimal currentBalance, DateOnly today)
    {
        if (promisedDate is not { } date) return null;
        if (currentBalance <= 0 || paidSinceContact >= PromiseTarget(promisedAmount, outstandingAmount, overdueAmount))
            return DebtPromiseStatus.Kept;
        return date < today ? DebtPromiseStatus.Broken : DebtPromiseStatus.Waiting;
    }
}
