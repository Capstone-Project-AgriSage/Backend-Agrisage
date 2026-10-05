using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Debt;

public sealed class DebtService(IAgriSageDbContext db, IRowLockService locks, IDateTimeProvider clock,
    ICurrentUserService currentUser, CustomerWrites access, AuditTrail audit,
    OrderQueries orders, PaymentQueries payments) : IDebtService
{
    private DateOnly Today => BusinessCalendar.Today(clock.UtcNow);

    // Store filtering happens before projection/pagination. IQueryable is private to Application.
    private IQueryable<EntryRow> Rows(Guid store) =>
        from e in db.DebtEntries.AsNoTracking()
        join a in db.DebtAccounts.AsNoTracking() on e.DebtAccountId equals a.Id
        where a.StoreId == store
        select new EntryRow
        {
            Id = e.Id, AccountId = a.Id, FarmerId = a.FarmerProfileId, Number = e.EntryNumber,
            FullName = a.FarmerProfile.User.FullName, Phone = a.FarmerProfile.User.PhoneNumber,
            OrderId = e.OrderId, OrderNumber = db.Orders.Where(o => o.Id == e.OrderId && o.StoreId == store).Select(o => o.OrderNumber).FirstOrDefault(),
            Source = e.SourceType, Original = e.OriginalAmount, Outstanding = e.OutstandingAmount,
            Due = e.DueDate, Status = e.Status, CreatedAt = e.CreatedAt,
            Paid = db.DebtTransactions.Where(t => t.DebtEntryId == e.Id && t.TransactionType == DebtTransactionType.Payment
                && t.Status == DebtTransactionStatus.Posted).Sum(t => (decimal?)-t.AmountDelta) ?? 0m
        };

    public async Task<PagedResult<DebtEntryListItem>> EntriesAsync(DebtEntryListRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var q = Rows(store);
        if (request.FarmerProfileId is { } farmer) q = q.Where(e => e.FarmerId == farmer);
        if (request.OrderId is { } order) q = q.Where(e => e.OrderId == order);
        if (EnumText.TryParse<DebtEntryStatus>(request.Status, out var status)) q = q.Where(e => e.Status == status);
        var today = Today;
        if (request.OverdueOnly) q = q.Where(e => e.Outstanding > 0 && e.Due < today);
        if (request.DueFrom is { } dueFrom) q = q.Where(e => e.Due >= dueFrom);
        if (request.DueTo is { } dueTo) q = q.Where(e => e.Due <= dueTo);
        if (request.FromDate is { } from) { var at = BusinessCalendar.StartOfDay(from); q = q.Where(e => e.CreatedAt >= at); }
        if (request.ToDate is { } to) { var at = BusinessCalendar.StartOfDay(to.AddDays(1)); q = q.Where(e => e.CreatedAt < at); }
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            q = q.Where(e => e.FullName.ToLower().Contains(term) || (e.Phone != null && e.Phone.Contains(term))
                || e.Number.ToLower().Contains(term) || (e.OrderNumber != null && e.OrderNumber.ToLower().Contains(term)));
        }
        var count = await q.LongCountAsync(token);
        IOrderedQueryable<EntryRow> sorted = request.SortBy.ToUpperInvariant() switch
        {
            "DUEDATE" => request.Descending ? q.OrderByDescending(e => e.Due) : q.OrderBy(e => e.Due),
            "OUTSTANDINGAMOUNT" => request.Descending ? q.OrderByDescending(e => e.Outstanding) : q.OrderBy(e => e.Outstanding),
            // Overdue day count is monotonic in due date; non-overdue entries form the zero bucket.
            "DAYSOVERDUE" => request.Descending ? q.OrderBy(e => e.Outstanding > 0 && e.Due < today ? e.Due : DateOnly.MaxValue)
                : q.OrderByDescending(e => e.Outstanding > 0 && e.Due < today ? e.Due : DateOnly.MaxValue),
            _ => request.Descending ? q.OrderByDescending(e => e.CreatedAt) : q.OrderBy(e => e.CreatedAt)
        };
        var rows = await sorted.ThenBy(e => e.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return new(rows.Select(e => Map(e, today)).ToList(), request.Page, request.PageSize, count);
    }

    public async Task<DebtEntryResponse> EntryAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var row = await Rows(store).SingleOrDefaultAsync(e => e.Id == id, token) ?? throw new NotFoundException("Debt entry", id);
        var entry = await db.DebtEntries.AsNoTracking().Include(e => e.Actions).SingleAsync(e => e.Id == id, token);
        var account = await db.DebtAccounts.AsNoTracking().SingleAsync(a => a.Id == row.AccountId, token);
        var profile = await db.FarmerCreditProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.StoreId == store && p.FarmerProfileId == row.FarmerId, token);
        var reserved = await db.CreditReservations.AsNoTracking().Where(r => r.StoreId == store
            && db.FarmerCreditProfiles.Any(p => p.Id == r.FarmerCreditProfileId && p.FarmerProfileId == row.FarmerId && p.StoreId == store)
            && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed))
            .SumAsync(r => (decimal?)(r.AmountReserved - r.AmountConsumed - r.AmountReleased), token) ?? 0;
        var transactions = await db.DebtTransactions.AsNoTracking().Where(t => t.DebtAccountId == row.AccountId && t.DebtEntryId == id)
            .OrderBy(t => t.OccurredAt).ThenBy(t => t.Id).ToListAsync(token);
        var paymentIds = await db.PaymentAllocations.AsNoTracking().Where(a => a.DebtEntryId == id).Select(a => a.PaymentId).Distinct().ToListAsync(token);
        var history = new List<PaymentResponse>();
        foreach (var paymentId in paymentIds) history.Add(await payments.GetAsync(paymentId, row.FarmerId, token));
        var summary = Map(row, Today);
        return new(id, row.Number, EnumText.Format(row.Source), entry.OrderId, row.OrderNumber, entry.DeliveryId,
            entry.DeliveryAttemptId, entry.SourceStockMovementId, entry.FulfillmentValue, entry.PrepaymentAppliedAmount,
            entry.OriginalAmount, row.Paid, entry.OutstandingAmount, entry.DueDate, summary.IsOverdue, summary.OverdueDays,
            EnumText.Format(entry.Status), new(row.FarmerId, row.FullName, row.Phone, profile?.CreditLimit ?? 0,
                account.CurrentBalance, reserved, Math.Max(0, profile?.CalculateAvailableCredit(account.CurrentBalance, reserved) ?? 0)),
            row.OrderId is { } order ? await orders.GetAsync(order, token) : null,
            entry.Actions.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => new DebtActionResponse(a.Id, EnumText.Format(a.ActionType),
                a.Reason, a.AdjustmentAmount, a.OldDueDate, a.NewDueDate, a.CreatedBy, a.CreatedAt)).ToList(),
            transactions.Select(MapTransaction).ToList(), history, entry.CreatedAt);
    }

    public async Task<DebtAccountResponse> AccountAsync(Guid farmerId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var a = await db.DebtAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.StoreId == store && a.FarmerProfileId == farmerId, token)
            ?? throw new NotFoundException("Customer debt account", farmerId);
        var entries = db.DebtEntries.AsNoTracking().Where(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0);
        var today = Today;
        return new(a.Id, farmerId, EnumText.Format(a.Status), a.CurrentBalance,
            await entries.Where(e => e.DueDate < today).SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0,
            await entries.CountAsync(token), await entries.Select(e => (DateOnly?)e.DueDate).MinAsync(token), a.LastTransactionAt, a.Version);
    }

    public async Task<PagedResult<DebtAccountListItem>> AccountsAsync(DebtAccountListRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var today = Today;
        var q = db.DebtAccounts.AsNoTracking().Where(a => a.StoreId == store);
        if (request.HasOutstanding is { } has) q = has ? q.Where(a => a.CurrentBalance > 0) : q.Where(a => a.CurrentBalance == 0);
        if (request.OverdueOnly) q = q.Where(a => db.DebtEntries.Any(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0 && e.DueDate < today));
        if (Texts.Clean(request.Search) is { } search)
        { var term = search.ToLowerInvariant(); q = q.Where(a => a.FarmerProfile.User.FullName.ToLower().Contains(term)
            || (a.FarmerProfile.User.PhoneNumber != null && a.FarmerProfile.User.PhoneNumber.Contains(term))); }
        var count = await q.LongCountAsync(token);
        var rows = await q.OrderByDescending(a => a.CurrentBalance).ThenBy(a => a.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(a => new
            {
                a.Id, a.FarmerProfileId, a.FarmerProfile.User.FullName, a.FarmerProfile.User.PhoneNumber, a.CurrentBalance,
                Overdue = db.DebtEntries.Where(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0 && e.DueDate < today)
                    .Sum(e => (decimal?)e.OutstandingAmount) ?? 0,
                Oldest = db.DebtEntries.Where(e => e.DebtAccountId == a.Id && e.OutstandingAmount > 0).Min(e => (DateOnly?)e.DueDate),
                GroupId = db.CustomerGroupAssignments.Where(g => g.FarmerProfileId == a.FarmerProfileId && g.EffectiveTo == null && g.CustomerGroup.StoreId == store)
                    .Select(g => (Guid?)g.CustomerGroupId).FirstOrDefault()
            }).ToListAsync(token);
        var groups = await db.CustomerGroups.AsNoTracking().Where(g => g.StoreId == store)
            .Select(g => new { Reference = new CustomerReference(g.Id, g.Code, g.Name), g.IsDefault }).ToListAsync(token);
        return new(rows.Select(a => new DebtAccountListItem(a.Id, a.FarmerProfileId, a.FullName, a.PhoneNumber,
            groups.FirstOrDefault(g => a.GroupId != null ? g.Reference.Id == a.GroupId : g.IsDefault)?.Reference,
            a.CurrentBalance, a.Overdue, a.Oldest)).ToList(), request.Page, request.PageSize, count);
    }

    public async Task<PagedResult<DebtTransactionResponse>> TransactionsAsync(Guid farmerId, DebtTransactionListRequest request, CancellationToken token)
    {
        var account = await AccountAsync(farmerId, token);
        var q = db.DebtTransactions.AsNoTracking().Where(t => t.DebtAccountId == account.Id);
        if (request.FromDate is { } from) { var at = BusinessCalendar.StartOfDay(from); q = q.Where(t => t.OccurredAt >= at); }
        if (request.ToDate is { } to) { var at = BusinessCalendar.StartOfDay(to.AddDays(1)); q = q.Where(t => t.OccurredAt < at); }
        var count = await q.LongCountAsync(token);
        var rows = await q.OrderByDescending(t => t.OccurredAt).ThenBy(t => t.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return new(rows.Select(MapTransaction).ToList(), request.Page, request.PageSize, count);
    }

    public async Task<AllocationPreviewResponse> PreviewAsync(Guid farmerId, decimal amount, CancellationToken token)
    {
        if (amount <= 0 || !Credit.CreditMoney.Valid(amount)) throw new BusinessRuleException("Amount must be positive money with at most two decimal places.");
        var account = await AccountAsync(farmerId, token);
        var entries = await db.DebtEntries.AsNoTracking().Where(e => e.DebtAccountId == account.Id && e.OutstandingAmount > 0
            && e.Status != DebtEntryStatus.Paid && e.Status != DebtEntryStatus.Cancelled).OrderBy(e => e.DueDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.EntryNumber, e.DueDate, e.OutstandingAmount }).ToListAsync(token);
        var left = amount;
        var result = new List<AllocationPreviewItem>();
        foreach (var e in entries)
        { if (left == 0) break; var take = Math.Min(left, e.OutstandingAmount); result.Add(new(e.Id, e.EntryNumber, e.DueDate, e.OutstandingAmount, take)); left -= take; }
        return new(amount, result, left);
    }

    public async Task<DebtEntryResponse> ActionAsync(Guid id, string action, string reason, decimal? amount, DateOnly? dueDate, CancellationToken token)
    {
        var actor = access.Actor(manage: action is "ADJUST" or "CANCEL");
        return await ActionCoreAsync(id, action, reason, amount, dueDate, actor, token);
    }

    private async Task<DebtEntryResponse> ActionCoreAsync(Guid id, string action, string reason, decimal? amount,
        DateOnly? dueDate, Guid actor, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Debt actions require a reason.");
        var store = await ActiveStore.GetIdAsync(db, token);
        var row = await Rows(store).SingleOrDefaultAsync(e => e.Id == id, token) ?? throw new NotFoundException("Debt entry", id);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockFarmerProfileAsync(row.FarmerId, token);
        var account = await db.DebtAccounts.SingleAsync(a => a.Id == row.AccountId, token);
        var entry = await db.DebtEntries.Include(e => e.Actions).SingleAsync(e => e.Id == id, token);
        switch (action)
        {
            case "DISPUTE": entry.Dispute(reason.Trim(), actor); break;
            case "KEEP": entry.Keep(reason.Trim(), actor); break;
            case "CHANGE_DUE_DATE":
                if (dueDate == null || dueDate < Today) throw new BusinessRuleException("The new due date cannot be in the past.");
                entry.ChangeDueDate(dueDate.Value, reason.Trim(), actor); break;
            case "ADJUST": db.DebtTransactions.Add(account.AdjustEntry(entry, amount ?? 0, reason.Trim(), actor, clock.UtcNow)); break;
            case "CANCEL": db.DebtTransactions.Add(account.CancelEntry(entry, reason.Trim(), actor, clock.UtcNow)); break;
            default: throw new BusinessRuleException("Unknown debt action.");
        }
        foreach (var a in entry.Actions.Where(a => db.DebtEntryActions.Entry(a).State == EntityState.Detached)) db.DebtEntryActions.Add(a);
        audit.Record($"DEBT_{action}", "DEBT_ENTRY", id, store, newValues: new { entry.OutstandingAmount, entry.DueDate, Status = EnumText.Format(entry.Status) }, reason: reason);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await EntryAsync(id, token);
    }

    public async Task<DebtEntryResponse> ManualAsync(Guid farmerId, ManualDebtEntryRequest request, CancellationToken token)
    {
        var actor = access.Actor(manage: true);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new BusinessRuleException("Manual debt requires a reason.");
        var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockFarmerProfileAsync(farmerId, token);
        await access.FindAsync(farmerId, token);
        var account = await db.DebtAccounts.SingleOrDefaultAsync(a => a.StoreId == store && a.FarmerProfileId == farmerId, token);
        if (account == null) { account = new DebtAccount(store, farmerId); db.DebtAccounts.Add(account); }
        var number = await DocumentNumbers.NextAsync(db.DebtEntries.IgnoreQueryFilters().AsNoTracking().Select(e => e.EntryNumber),
            DocumentNumbers.DebtEntry, Today, token);
        var posting = account.CreateManualAdjustmentEntry(number, request.Amount, request.DueDate, actor, clock.UtcNow, note: request.Reason.Trim());
        db.DebtEntries.Add(posting.Entry); db.DebtTransactions.Add(posting.Transaction);
        audit.Record("DEBT_MANUAL_ENTRY", "DEBT_ENTRY", posting.Entry.Id, store, newValues: new { request.Amount, request.DueDate }, reason: request.Reason);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return await EntryAsync(posting.Entry.Id, token);
    }

    public async Task<DebtDashboardResponse> DashboardAsync(CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var today = Today;
        var dayStart = BusinessCalendar.StartOfDay(today);
        var monthStart = BusinessCalendar.StartOfDay(new DateOnly(today.Year, today.Month, 1));
        var tomorrow = BusinessCalendar.StartOfDay(today.AddDays(1));
        var q = Rows(store);
        var overdue = q.Where(e => e.Outstanding > 0 && e.Due < today);
        var collected = from t in db.DebtTransactions.AsNoTracking()
                        join a in db.DebtAccounts.AsNoTracking() on t.DebtAccountId equals a.Id
                        where a.StoreId == store && t.TransactionType == DebtTransactionType.Payment && t.Status == DebtTransactionStatus.Posted
                        select t;
        var top = await AccountsAsync(new DebtAccountListRequest { HasOutstanding = true, PageSize = 10 }, token);
        var recent = await payments.ListAsync(new PaymentListRequest { PaymentContext = "DEBT_REPAYMENT", Status = "PAID", PageSize = 10 }, null, token);
        var late = await EntriesAsync(new DebtEntryListRequest { OverdueOnly = true, SortBy = "DueDate", Descending = false, PageSize = 10 }, token);
        return new(await q.SumAsync(e => (decimal?)e.Outstanding, token) ?? 0, await overdue.SumAsync(e => (decimal?)e.Outstanding, token) ?? 0,
            await collected.Where(t => t.OccurredAt >= dayStart && t.OccurredAt < tomorrow).SumAsync(t => (decimal?)-t.AmountDelta, token) ?? 0,
            await collected.Where(t => t.OccurredAt >= monthStart && t.OccurredAt < tomorrow).SumAsync(t => (decimal?)-t.AmountDelta, token) ?? 0,
            await q.Where(e => e.Outstanding > 0).Select(e => e.FarmerId).Distinct().CountAsync(token),
            await overdue.Select(e => e.FarmerId).Distinct().CountAsync(token), top.Items, recent.Items, late.Items);
    }

    public async Task<Guid> OwnCustomerAsync(CancellationToken token)
    {
        var actor = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        return await db.FarmerProfiles.AsNoTracking().Where(f => f.UserId == actor && f.User.Role.Code == RoleCode.Farmer)
            .Select(f => (Guid?)f.Id).SingleOrDefaultAsync(token) ?? throw new ForbiddenException();
    }
    public async Task RequireOwnedEntryAsync(Guid entryId, Guid farmerId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        if (!await Rows(store).AnyAsync(e => e.Id == entryId && e.FarmerId == farmerId, token)) throw new NotFoundException("Debt entry", entryId);
    }
    public async Task<DebtEntryResponse> DisputeOwnAsync(Guid id, string reason, CancellationToken token)
    {
        var own = await OwnCustomerAsync(token); await RequireOwnedEntryAsync(id, own, token);
        return await ActionCoreAsync(id, "DISPUTE", reason, null, null, currentUser.UserId!.Value, token);
    }

    private static DebtEntryListItem Map(EntryRow e, DateOnly today) => new(e.Id, e.Number, e.FarmerId, e.FullName, e.Phone,
        EnumText.Format(e.Source), e.OrderNumber, e.Original, e.Paid, e.Outstanding, e.Due, e.Outstanding > 0 && e.Due < today,
        e.Outstanding > 0 ? Math.Max(0, today.DayNumber - e.Due.DayNumber) : 0, EnumText.Format(e.Status), e.CreatedAt);
    private static DebtTransactionResponse MapTransaction(DebtTransaction t) => new(t.Id, t.DebtEntryId, EnumText.Format(t.TransactionType),
        t.AmountDelta, t.BalanceAfter - t.AmountDelta, t.BalanceAfter, t.OccurredAt, EnumText.Format(t.Status), t.PaymentAllocationId,
        t.SalesReturnId, t.DebtEntryActionId, t.Note, t.CreatedBy);
    private sealed class EntryRow
    {
        public Guid Id { get; init; } public Guid AccountId { get; init; } public Guid FarmerId { get; init; }
        public string Number { get; init; } = ""; public string FullName { get; init; } = ""; public string? Phone { get; init; }
        public Guid? OrderId { get; init; } public string? OrderNumber { get; init; } public DebtEntrySourceType Source { get; init; }
        public decimal Original { get; init; } public decimal Paid { get; init; } public decimal Outstanding { get; init; }
        public DateOnly Due { get; init; } public DebtEntryStatus Status { get; init; } public DateTimeOffset CreatedAt { get; init; }
    }
}
