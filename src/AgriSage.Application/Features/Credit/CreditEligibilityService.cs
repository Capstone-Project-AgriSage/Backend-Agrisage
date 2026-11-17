using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Credit;

public sealed record CreditEligibilityRequest(decimal OrderAmount);
public sealed record CreditEligibilityResponse(Guid CustomerId, decimal CreditLimit, decimal CurrentOutstandingDebt,
    decimal ReservedCredit, decimal AvailableCredit, decimal OrderAmount, decimal AvailableCreditAfterOrder,
    bool HasOverdueDebt, decimal OverdueAmount, bool Eligible, string ReasonCode, string Message,
    DebtSeasonReference? ExpectedCropSeason = null, DateOnly? ExpectedDueDate = null, string? DueRule = null);

public interface ICreditEligibilityService
{
    Task<CreditEligibilityResponse> CheckAsync(Guid customerId, decimal orderAmount, CancellationToken token);
}

public sealed class CreditEligibilityRequestValidator : AbstractValidator<CreditEligibilityRequest>
{
    public CreditEligibilityRequestValidator() => RuleFor(r => r.OrderAmount).Must(CreditMoney.Valid);
}

// Advisory reads do not reserve credit. Confirmation repeats this check under the customer row lock.
// Overdue debt is reported (HasOverdueDebt, OverdueAmount) but never refuses credit by itself: the approved debt policy (CS-10)
// forbids the system from blocking or limiting credit on its own; only the store owner suspends or blocks, with a reason.
public sealed class CreditEligibilityService(IAgriSageDbContext db, IDateTimeProvider clock) : ICreditEligibilityService
{
    public async Task<CreditEligibilityResponse> CheckAsync(Guid customerId, decimal orderAmount, CancellationToken token)
    {
        if (!CreditMoney.Valid(orderAmount)) throw new BusinessRuleException("Order amount must be non-negative money with at most two decimal places.");
        var store = await ActiveStore.GetIdAsync(db, token);
        var customer = await db.FarmerProfiles.AsNoTracking().Where(f => f.Id == customerId && f.User.Role.Code == RoleCode.Farmer)
            .Select(f => new { f.User.Status }).FirstOrDefaultAsync(token);
        var profile = await db.FarmerCreditProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.StoreId == store && p.FarmerProfileId == customerId, token);
        var account = await db.DebtAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.StoreId == store && a.FarmerProfileId == customerId, token);
        var reserved = profile == null ? 0m : await db.CreditReservations.AsNoTracking()
            .Where(r => r.StoreId == store && r.FarmerCreditProfileId == profile.Id
                && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed))
            .SumAsync(r => (decimal?)(r.AmountReserved - r.AmountConsumed - r.AmountReleased), token) ?? 0m;
        var today = BusinessCalendar.Today(clock.UtcNow);
        var overdue = account == null ? 0m : await db.DebtEntries.AsNoTracking()
            .Where(e => e.DebtAccountId == account.Id && e.OutstandingAmount > 0 && e.DueDate < today)
            .SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0m;
        var debt = account?.CurrentBalance ?? 0m;
        var limit = profile?.CreditLimit ?? 0m;
        var available = profile?.CalculateAvailableCredit(debt, reserved) ?? 0m;
        var reason = customer == null ? "CUSTOMER_NOT_FOUND"
            : customer.Status != UserStatus.Active ? "CUSTOMER_INACTIVE"
            : profile?.Status != FarmerCreditProfileStatus.Active || account?.Status != DebtAccountStatus.Active ? "CREDIT_DISABLED"
            : limit <= 0 ? "NO_CREDIT_LIMIT"
            : profile.CreditTierId == null ? "NO_CREDIT_TERM"
            : orderAmount > available ? "INSUFFICIENT_CREDIT" : "ELIGIBLE";
        // F3.13 (CS-04): the season an order confirmed today would join and the due date it would get if delivered today.
        // An estimate: the debt and its real due date are created on delivery (D15).
        DebtSeasonReference? expectedSeason = null;
        DateOnly? expectedDue = null;
        string? dueRule = null;
        var termDays = profile?.CreditTierId is { } tierId
            ? await db.CreditTiers.AsNoTracking().Where(t => t.Id == tierId).Select(t => (int?)t.DefaultPaymentTermDays).SingleOrDefaultAsync(token)
            : null;
        if (termDays is { } days)
        {
            var season = await DebtDueLookup.OpenSeasonOnAsync(db, store, today, token);
            var cap = season is null ? null : await DebtDueLookup.CapAsync(db, store, token);
            var due = DebtDueCalculator.Calculate(season, today, days, cap);
            (expectedSeason, expectedDue, dueRule) = (season is null ? null : new DebtSeasonReference(season.Id, season.Code, season.Name), due.DueDate, due.Rule);
        }

        return new(customerId, limit, debt, reserved, Math.Max(0, available), orderAmount,
            Math.Max(0, available - orderAmount), overdue > 0, overdue, reason == "ELIGIBLE", reason,
            reason == "ELIGIBLE" ? "Customer is eligible for this credit amount." : $"Credit refused: {reason}.",
            expectedSeason, expectedDue, dueRule);
    }

    public static void RequireEligible(CreditEligibilityResponse result)
    {
        if (!result.Eligible) throw new BusinessRuleException(result.Message,
            new Dictionary<string, string[]> { ["credit"] = [result.ReasonCode] });
    }
}
