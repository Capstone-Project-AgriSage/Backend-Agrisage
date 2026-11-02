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

    // Due date of a debt created on fulfilledDay for a season due on seasonDue. D4: an optional cap (store policy,
    // off by default) shortens it to fulfilledDay + cap. Proposal pending lead: a delivery made after the season due date
    // is due on its own delivery day rather than already overdue.
    public static DateOnly DueDate(DateOnly seasonDue, DateOnly fulfilledDay, int? maxCreditTermDays)
    {
        if (maxCreditTermDays is <= 0)
        {
            throw new DomainException("The maximum credit term must be at least one day.");
        }

        var due = maxCreditTermDays is { } cap && fulfilledDay.AddDays(cap) < seasonDue ? fulfilledDay.AddDays(cap) : seasonDue;
        return due < fulfilledDay ? fulfilledDay : due;
    }
}
