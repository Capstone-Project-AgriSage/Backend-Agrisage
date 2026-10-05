using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Features.Debt.Enums;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

namespace AgriSage.Application.Features.Reports;

public sealed record DebtAgingRequest(DateOnly? AsOf = null, Guid? CustomerGroupId = null);
public sealed record DebtCollectionRequest
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string GroupBy { get; init; } = "DAY";
}
public sealed record DebtAgingTotals(decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);
public sealed record DebtAgingRow(Guid FarmerProfileId, string FullName, string? PhoneNumber, CustomerReference? CustomerGroup,
    decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);
public sealed record DebtAgingReportResponse(DateOnly AsOf, IReadOnlyList<DebtAgingRow> Rows, DebtAgingTotals Totals);
public sealed record DebtCollectionRow(string Key, string Label, int PaymentCount, decimal CollectedAmount);
public sealed record DebtCollectionReportResponse(DateOnly FromDate, DateOnly ToDate, string GroupBy,
    IReadOnlyList<DebtCollectionRow> Rows, DebtCollectionRow Totals);
public sealed record DebtByGroupRow(CustomerReference? CustomerGroup, int CustomersWithDebt, decimal Outstanding,
    decimal OverdueAmount, decimal TotalCreditLimit, decimal? Utilization);
public sealed record DebtByGroupReportResponse(IReadOnlyList<DebtByGroupRow> Rows);
public interface IDebtReportService
{
    Task<DebtAgingReportResponse> AgingAsync(DebtAgingRequest request, CancellationToken token);
    Task<DebtCollectionReportResponse> CollectionsAsync(DebtCollectionRequest request, CancellationToken token);
    Task<DebtByGroupReportResponse> GroupsAsync(CancellationToken token);
}
public sealed class DebtCollectionRequestValidator : AbstractValidator<DebtCollectionRequest>
{
    public DebtCollectionRequestValidator()
    {
        RuleFor(r => r.FromDate).NotNull(); RuleFor(r => r.ToDate).NotNull();
        RuleFor(r => r.ToDate).GreaterThanOrEqualTo(r => r.FromDate).When(r => r.FromDate != null && r.ToDate != null);
        RuleFor(r => r).Must(r => r.ToDate!.Value.DayNumber - r.FromDate!.Value.DayNumber + 1 <= 366)
            .When(r => r.FromDate != null && r.ToDate != null).WithName("ToDate");
        RuleFor(r => r.GroupBy).Must(g => g != null && new[] { "DAY", "METHOD", "STAFF" }.Contains(g.ToUpperInvariant()));
    }
}

public sealed class DebtReportService(IAgriSageDbContext db, IDateTimeProvider clock) : IDebtReportService
{
    public async Task<DebtAgingReportResponse> AgingAsync(DebtAgingRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var day = request.AsOf ?? BusinessCalendar.Today(clock.UtcNow);
        var end = BusinessCalendar.StartOfDay(day.AddDays(1));
        var balances = await (from t in db.DebtTransactions.AsNoTracking()
                              join a in db.DebtAccounts.AsNoTracking() on t.DebtAccountId equals a.Id
                              where a.StoreId == store && t.DebtEntryId != null && t.Status == DebtTransactionStatus.Posted && t.OccurredAt < end
                              group t by t.DebtEntryId into g
                              select new { Id = g.Key!.Value, Amount = g.Sum(t => t.AmountDelta) }).Where(g => g.Amount > 0).ToDictionaryAsync(g => g.Id, token);
        var ids = balances.Keys.ToList();
        var entries = await (from e in db.DebtEntries.AsNoTracking()
                             join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
                             where a.StoreId == store && ids.Contains(e.Id)
                             select new
                             {
                                 e.Id, a.FarmerProfileId, a.FarmerProfile.User.FullName, a.FarmerProfile.User.PhoneNumber,
                                 // Reverse later due-date edits for a historical aging picture.
                                 Due = e.Actions.Where(x => x.ActionType == DebtEntryActionType.ChangeDueDate && x.CreatedAt >= end)
                                     .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.OldDueDate).FirstOrDefault() ?? e.DueDate
                             }).ToListAsync(token);
        var groups = await CustomerGroupsAsync(store, end.AddTicks(-1), token);
        var rows = entries.GroupBy(e => e.FarmerProfileId).Select(g =>
        {
            var c = g.First();
            decimal Bucket(int min, int max) => g.Where(e => day.DayNumber - e.Due.DayNumber >= min && day.DayNumber - e.Due.DayNumber <= max).Sum(e => balances[e.Id].Amount);
            return new DebtAgingRow(g.Key, c.FullName, c.PhoneNumber, groups.GetValueOrDefault(g.Key),
                Bucket(int.MinValue, 0), Bucket(1, 30), Bucket(31, 60), Bucket(61, 90), Bucket(91, int.MaxValue), g.Sum(e => balances[e.Id].Amount));
        }).Where(r => request.CustomerGroupId == null || r.CustomerGroup?.Id == request.CustomerGroupId)
            .OrderByDescending(r => r.Total).ThenBy(r => r.FarmerProfileId).ToList();
        return new(day, rows, new(rows.Sum(r => r.NotDue), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90), rows.Sum(r => r.Over90), rows.Sum(r => r.Total)));
    }

    public async Task<DebtCollectionReportResponse> CollectionsAsync(DebtCollectionRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var from = request.FromDate!.Value; var to = request.ToDate!.Value;
        var start = BusinessCalendar.StartOfDay(from); var end = BusinessCalendar.StartOfDay(to.AddDays(1));
        var rows = await (from t in db.DebtTransactions.AsNoTracking()
                          join a in db.PaymentAllocations.AsNoTracking() on t.PaymentAllocationId equals a.Id
                          join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                          where p.StoreId == store && t.TransactionType == DebtTransactionType.Payment
                              && t.Status == DebtTransactionStatus.Posted && t.OccurredAt >= start && t.OccurredAt < end
                          select new { p.Id, p.PaymentMethod, p.ConfirmedBy, t.OccurredAt, Amount = -t.AmountDelta }).ToListAsync(token);
        var actors = rows.Where(r => r.ConfirmedBy != null).Select(r => r.ConfirmedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => actors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, token);
        var groupBy = request.GroupBy.ToUpperInvariant();
        string Key(DateTimeOffset at, Domain.Features.Payments.Enums.PaymentMethod method, Guid? actor) => groupBy switch
        { "METHOD" => PaymentText.Format(method), "STAFF" => actor?.ToString() ?? "PAYOS", _ => BusinessCalendar.Today(at).ToString("yyyy-MM-dd") };
        var grouped = rows.GroupBy(r => Key(r.OccurredAt, r.PaymentMethod, r.ConfirmedBy)).OrderBy(g => g.Key)
            .Select(g => new DebtCollectionRow(g.Key, groupBy == "STAFF" && Guid.TryParse(g.Key, out var id) ? names.GetValueOrDefault(id, g.Key) : g.Key,
                g.Select(r => r.Id).Distinct().Count(), g.Sum(r => r.Amount))).ToList();
        return new(from, to, groupBy, grouped, new("TOTAL", "Total", rows.Select(r => r.Id).Distinct().Count(), rows.Sum(r => r.Amount)));
    }

    public async Task<DebtByGroupReportResponse> GroupsAsync(CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var now = clock.UtcNow; var today = BusinessCalendar.Today(now);
        var groups = await CustomerGroupsAsync(store, now, token);
        var profiles = await db.FarmerCreditProfiles.AsNoTracking().Where(p => p.StoreId == store)
            .Select(p => new { p.FarmerProfileId, p.CreditLimit }).ToListAsync(token);
        var debts = await db.DebtAccounts.AsNoTracking().Where(a => a.StoreId == store)
            .Select(a => new { a.FarmerProfileId, a.CurrentBalance, Overdue = db.DebtEntries.Where(e => e.DebtAccountId == a.Id && e.DueDate < today)
                .Sum(e => (decimal?)e.OutstandingAmount) ?? 0m }).ToListAsync(token);
        var rows = debts.Select(d => d.FarmerProfileId).Union(profiles.Select(p => p.FarmerProfileId))
            .GroupBy(id => groups.GetValueOrDefault(id)?.Id).Select(g =>
            {
                var ids = g.ToHashSet(); var outstanding = debts.Where(d => ids.Contains(d.FarmerProfileId)).Sum(d => d.CurrentBalance);
                var limit = profiles.Where(p => ids.Contains(p.FarmerProfileId)).Sum(p => p.CreditLimit);
                return new DebtByGroupRow(groups.GetValueOrDefault(g.First()), debts.Count(d => ids.Contains(d.FarmerProfileId) && d.CurrentBalance > 0),
                    outstanding, debts.Where(d => ids.Contains(d.FarmerProfileId)).Sum(d => d.Overdue), limit, limit > 0 ? outstanding / limit : null);
            }).OrderByDescending(r => r.Outstanding).ToList();
        return new(rows);
    }

    private async Task<Dictionary<Guid, CustomerReference?>> CustomerGroupsAsync(Guid store, DateTimeOffset at, CancellationToken token)
    {
        var defaultGroup = await db.CustomerGroups.AsNoTracking().Where(g => g.StoreId == store && g.IsDefault)
            .Select(g => new CustomerReference(g.Id, g.Code, g.Name)).FirstOrDefaultAsync(token);
        var assignments = await db.CustomerGroupAssignments.AsNoTracking().Where(a => a.CustomerGroup.StoreId == store
            && a.EffectiveFrom <= at && (a.EffectiveTo == null || a.EffectiveTo > at))
            .Select(a => new { a.FarmerProfileId, Group = new CustomerReference(a.CustomerGroup.Id, a.CustomerGroup.Code, a.CustomerGroup.Name) }).ToListAsync(token);
        var ids = await db.FarmerProfiles.AsNoTracking().Select(f => f.Id).ToListAsync(token);
        var map = assignments.ToDictionary(a => a.FarmerProfileId, a => a.Group);
        return ids.ToDictionary(id => id, id => map.GetValueOrDefault(id) ?? defaultGroup);
    }
}
