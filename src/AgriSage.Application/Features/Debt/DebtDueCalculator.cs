using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Debt;
using AgriSage.Domain.Features.Debt.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// F3.13: the due date of a credit debt and the values it was computed from (D17). Basis is the jsonb stored in
// debt_entries.due_basis, so a later change of the season or the credit tier never silently moves an existing debt.
public sealed record DebtDue(DateOnly DueDate, string Rule, string Basis);

public static class DebtDueCalculator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DebtDue Calculate(CropSeason? season, DateOnly fulfilledDay, int tierTermDays, int? maxCreditTermDays)
    {
        var due = SeasonCalendar.ComputeDue(season, fulfilledDay, tierTermDays, maxCreditTermDays);
        return new(due.DueDate, due.Rule, Serialize(due.Rule, season, fulfilledDay, tierTermDays, maxCreditTermDays, due.LateForSeason));
    }

    // A manual debt whose owner chose the due date (the season only classifies it).
    public static string ManualBasis(CropSeason season, DateOnly fulfilledDay) =>
        Serialize(DueRules.Manual, season, fulfilledDay, null, null, false);

    private static string Serialize(string rule, CropSeason? season, DateOnly fulfilledDay, int? termDays, int? capDays, bool late) =>
        JsonSerializer.Serialize(new
        {
            rule,
            cropSeasonId = season?.Id,
            seasonCode = season?.Code,
            harvestEnd = season?.HarvestEnd,
            graceDays = season?.GraceDays,
            seasonDueDate = season?.PaymentDueDate,
            termDays,
            capDays = rule == DueRules.Capped ? capDays : null,
            fulfilledOn = fulfilledDay,
            lateForSeason = late,
        }, Json);
}

// Reads shared by the order confirmation, the credit check and the debt posting.
public static class DebtDueLookup
{
    // The season whose order window contains the day (an order confirmed that day belongs to it), or null: then the
    // credit-tier term applies as before.
    public static async Task<CropSeason?> OpenSeasonOnAsync(IAgriSageDbContext db, Guid store, DateOnly day, CancellationToken token) =>
        SeasonCalendar.Resolve(await db.CropSeasons.AsNoTracking()
            .Where(s => s.StoreId == store && s.ClosedAt == null && s.OrderWindowStart <= day && day < s.HarvestStart)
            .ToListAsync(token), day);

    public static Task<int?> CapAsync(IAgriSageDbContext db, Guid store, CancellationToken token) =>
        db.StoreDebtPolicies.AsNoTracking().Where(p => p.StoreId == store).Select(p => p.MaxCreditTermDays).SingleOrDefaultAsync(token);
}
