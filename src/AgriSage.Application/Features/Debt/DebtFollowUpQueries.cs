using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Debt;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

// Read side of F3.23, shared by the follow-up routes and the debt lists and dashboard. Read only (AsNoTracking); the
// payments since each contact are summed in SQL, and only the promise label is worked out in memory (DebtFollowUpPolicy).
public sealed class DebtFollowUpQueries(IAgriSageDbContext db)
{
    // "Not reminded lately" on the owner dashboard: no record contacted within this many days.
    public const int WindowDays = 7;

    private sealed class Row
    {
        public Guid Id { get; init; }
        public Guid AccountId { get; init; }
        public DateTimeOffset ContactedAt { get; init; }
        public DebtFollowUpChannel Channel { get; init; }
        public DebtFollowUpOutcome Outcome { get; init; }
        public string? Note { get; init; }
        public DateOnly? PromisedDate { get; init; }
        public decimal? PromisedAmount { get; init; }
        public decimal OutstandingAmount { get; init; }
        public decimal OverdueAmount { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string? ByName { get; init; }
        public RoleCode? ByRole { get; init; }
        public decimal PaidSince { get; init; }
        public decimal Balance { get; init; }
    }

    private IQueryable<Row> Rows(IQueryable<DebtFollowUp> source) =>
        from f in source
        select new Row
        {
            Id = f.Id, AccountId = f.DebtAccountId, ContactedAt = f.ContactedAt, Channel = f.Channel, Outcome = f.Outcome,
            Note = f.Note, PromisedDate = f.PromisedDate, PromisedAmount = f.PromisedAmount,
            OutstandingAmount = f.OutstandingAmount, OverdueAmount = f.OverdueAmount, CreatedBy = f.CreatedBy,
            CreatedAt = f.CreatedAt,
            ByName = db.Users.IgnoreQueryFilters().Where(u => u.Id == f.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            ByRole = db.Users.IgnoreQueryFilters().Where(u => u.Id == f.CreatedBy).Select(u => (RoleCode?)u.Role.Code).FirstOrDefault(),
            // Posted PAYMENT ledger lines of the account since the contact (amount_delta is negative for a payment).
            PaidSince = db.DebtTransactions.Where(t => t.DebtAccountId == f.DebtAccountId
                    && t.TransactionType == DebtTransactionType.Payment && t.Status == DebtTransactionStatus.Posted
                    && t.OccurredAt >= f.ContactedAt)
                .Sum(t => (decimal?)-t.AmountDelta) ?? 0m,
            Balance = db.DebtAccounts.Where(a => a.Id == f.DebtAccountId).Select(a => a.CurrentBalance).FirstOrDefault()
        };

    private static DebtPromiseStatus? Status(Row row, DateOnly today) => DebtFollowUpPolicy.PromiseStatus(row.PromisedDate,
        row.PromisedAmount, row.OutstandingAmount, row.OverdueAmount, row.PaidSince, row.Balance, today);

    private static string? Format(DebtPromiseStatus? status) => status is { } value ? EnumText.Format(value) : null;

    private static DebtFollowUpResponse Map(Row row, DateOnly today) => new(row.Id, row.ContactedAt, EnumText.Format(row.Channel),
        EnumText.Format(row.Outcome), row.Note, row.PromisedDate, row.PromisedAmount, Format(Status(row, today)), row.PaidSince,
        row.OutstandingAmount, row.OverdueAmount,
        new DebtFollowUpActor(row.CreatedBy, row.ByName, row.ByRole is { } role ? EnumText.Format(role) : null), row.CreatedAt);

    // Latest contact first; a record of the same moment by creation order.
    private static IOrderedQueryable<DebtFollowUp> Newest(IQueryable<DebtFollowUp> source) =>
        source.OrderByDescending(f => f.ContactedAt).ThenByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id);

    public async Task<PagedResult<DebtFollowUpResponse>> PageAsync(Guid accountId, PaginationRequest request, DateOnly today,
        CancellationToken token)
    {
        var source = db.DebtFollowUps.AsNoTracking().Where(f => f.DebtAccountId == accountId);
        var total = await source.LongCountAsync(token);
        var rows = await Rows(Newest(source).Skip(request.Skip).Take(request.PageSize)).ToListAsync(token);
        return new(rows.OrderByDescending(r => r.ContactedAt).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => Map(r, today)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<DebtFollowUpResponse> GetAsync(Guid id, DateOnly today, CancellationToken token) =>
        Map(await Rows(db.DebtFollowUps.AsNoTracking().Where(f => f.Id == id)).SingleAsync(token), today);

    // The latest record of each account (by contact time), for the staff debt entry lists.
    public async Task<IReadOnlyDictionary<Guid, DebtFollowUpSummary>> LatestAsync(IReadOnlyCollection<Guid> accountIds,
        DateOnly today, CancellationToken token)
    {
        if (accountIds.Count == 0) return new Dictionary<Guid, DebtFollowUpSummary>();
        var ids = accountIds.ToList();
        var latestIds = await db.DebtFollowUps.AsNoTracking().Where(f => ids.Contains(f.DebtAccountId))
            .GroupBy(f => f.DebtAccountId)
            .Select(g => g.OrderByDescending(f => f.ContactedAt).ThenByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                .Select(f => f.Id).First())
            .ToListAsync(token);
        var rows = await Rows(db.DebtFollowUps.AsNoTracking().Where(f => latestIds.Contains(f.Id))).ToListAsync(token);
        return rows.ToDictionary(r => r.AccountId, r => new DebtFollowUpSummary(r.ContactedAt, EnumText.Format(r.Channel),
            EnumText.Format(r.Outcome), r.PromisedDate, Format(Status(r, today)), r.ByName));
    }

    // Owner dashboard (DEBT.READ_DASHBOARD): counts only, for the active store.
    public async Task<DebtFollowUpDashboard> DashboardAsync(Guid store, DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var since = now.AddDays(-WindowDays);
        var accounts = db.DebtAccounts.AsNoTracking().Where(a => a.StoreId == store);
        var overdue = accounts.Where(a => db.DebtEntries.Any(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0 && e.DueDate < today));
        var overdueCount = await overdue.CountAsync(token);
        var notContacted = await overdue.CountAsync(a => !db.DebtFollowUps.Any(f => f.DebtAccountId == a.Id && f.ContactedAt >= since), token);

        // The latest promise of each customer who still owes; whether it is broken needs the payments since it.
        var owing = accounts.Where(a => a.CurrentBalance > 0).Select(a => a.Id);
        var latestPromises = await db.DebtFollowUps.AsNoTracking()
            .Where(f => f.Outcome == DebtFollowUpOutcome.Promised && owing.Contains(f.DebtAccountId))
            .GroupBy(f => f.DebtAccountId)
            .Select(g => g.OrderByDescending(f => f.ContactedAt).ThenByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
                .Select(f => f.Id).First())
            .ToListAsync(token);
        var promises = await Rows(db.DebtFollowUps.AsNoTracking().Where(f => latestPromises.Contains(f.Id))).ToListAsync(token);
        var broken = promises.Count(r => Status(r, today) == DebtPromiseStatus.Broken);

        return new(WindowDays, overdueCount, notContacted, broken);
    }
}
