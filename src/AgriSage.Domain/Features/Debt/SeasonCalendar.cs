using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Entities;

namespace AgriSage.Domain.Features.Debt;

// Pure crop-season calendar rules (F3.8). All dates are Vietnam business days.
public static class SeasonCalendar
{
    // The open season whose order window [OrderWindowStart, HarvestStart) contains the date (the credit order's
    // confirmation day), or null: then the credit-tier term applies as before.
    public static CropSeason? Resolve(IEnumerable<CropSeason> seasons, DateOnly date) =>
        seasons.Where(s => !s.IsClosed && s.OrderWindowStart <= date && date < s.HarvestStart)
            .OrderBy(s => s.HarvestStart)
            .FirstOrDefault();

    // Seasons of one store chain without gap or overlap: each order window opens on the previous harvest start (D3).
    public static void ValidateSequence(IEnumerable<CropSeason> seasons)
    {
        var ordered = seasons.OrderBy(s => s.HarvestStart).ToList();
        if (ordered.Select(s => s.Code).Distinct(StringComparer.Ordinal).Count() != ordered.Count)
        {
            throw new DomainException("Season codes must be unique in the store.");
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].OrderWindowStart != ordered[i - 1].HarvestStart)
            {
                throw new DomainException(
                    $"Season '{ordered[i].Code}' must open its order window on {ordered[i - 1].HarvestStart:yyyy-MM-dd}, " +
                    $"the harvest start of '{ordered[i - 1].Code}' (no gap, no overlap).");
            }
        }
    }

    // A new season is only added after the last one; its order window opens on the last harvest start.
    public static DateOnly? NextOrderWindowStart(IEnumerable<CropSeason> seasons) =>
        seasons.Select(s => (DateOnly?)s.HarvestStart).Max();

    // Due date of a credit debt created on fulfilledDay (F3.13, D1, D4, D15; owner L3 choice B, 2026-11-03):
    //  - the order has a season not yet past its due date: the season's payment due date (SEASON). D4: an optional cap (store
    //    policy, off by default) shortens it to fulfilledDay + cap (CAPPED);
    //  - the delivery comes after the season's due date: it would be overdue on day one, so the customer gets the usual
    //    credit-tier term from the delivery day (TERM_DAYS, LateForSeason);
    //  - no season: the credit-tier term from the delivery day, as before (TERM_DAYS). The cap does not apply to the term.
    public static DueCalculation ComputeDue(CropSeason? season, DateOnly fulfilledDay, int tierTermDays, int? maxCreditTermDays)
    {
        if (maxCreditTermDays is <= 0)
        {
            throw new DomainException("The maximum credit term must be at least one day.");
        }

        if (tierTermDays < 0)
        {
            throw new DomainException("The credit term cannot be negative.");
        }

        if (season is null || season.PaymentDueDate < fulfilledDay)
        {
            return new(fulfilledDay.AddDays(tierTermDays), DueRules.TermDays, season is not null);
        }

        return maxCreditTermDays is { } cap && fulfilledDay.AddDays(cap) < season.PaymentDueDate
            ? new(fulfilledDay.AddDays(cap), DueRules.Capped, false)
            : new(season.PaymentDueDate, DueRules.Season, false);
    }
}

// How a due date was reached; stored with the debt (D17) and returned as dueRule.
public static class DueRules
{
    public const string Season = "SEASON";
    public const string Capped = "CAPPED";
    public const string TermDays = "TERM_DAYS";
    public const string Manual = "MANUAL";
}

// LateForSeason: the order has a season but it was delivered after the season's due date.
public readonly record struct DueCalculation(DateOnly DueDate, string Rule, bool LateForSeason);
