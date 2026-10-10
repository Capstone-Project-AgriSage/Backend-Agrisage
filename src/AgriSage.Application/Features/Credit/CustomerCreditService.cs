using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Credit;

// Profile/tier administration; order eligibility and postings use the shared credit/debt steps.
public sealed class CustomerCreditService(IAgriSageDbContext context, IRowLockService locks,
    IDatabaseErrorClassifier databaseErrors, CustomerWrites writes, AuditTrail audit, IDateTimeProvider clock) : ICustomerCreditService
{
    public async Task<CreditSummaryResponse> GetAsync(Guid farmerId, CancellationToken token)
    {
        writes.Actor();
        return await GetCoreAsync(farmerId, token);
    }

    public async Task<MyCreditSummaryResponse> GetOwnAsync(CancellationToken token)
    {
        var p = await GetCoreAsync(await writes.OwnCustomerAsync(token), token);
        return new(p.ProfileId, p.FarmerProfileId, p.Status, p.CreditTier, p.CreditLimit, p.PaymentTermDays,
            p.OutstandingReceivable, p.ReservedCredit, p.AvailableCredit);
    }

    private async Task<CreditSummaryResponse> GetCoreAsync(Guid farmerId, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var p = await context.FarmerCreditProfiles.AsNoTracking().Include(p => p.CreditTier)
            .FirstOrDefaultAsync(p => p.StoreId == store && p.FarmerProfileId == farmerId
                && p.FarmerProfile.User.Role.Code == AgriSage.Domain.Features.Identity.Enums.RoleCode.Farmer, token)
            ?? throw new NotFoundException("Customer credit profile", farmerId);
        var outstanding = await context.DebtAccounts.AsNoTracking().Where(a => a.StoreId == store && a.FarmerProfileId == farmerId)
            .Select(a => (decimal?)a.CurrentBalance).FirstOrDefaultAsync(token) ?? 0m;
        var reserved = await context.CreditReservations.AsNoTracking().Where(r => r.StoreId == store && r.FarmerCreditProfileId == p.Id
            && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed))
            .SumAsync(r => (decimal?)(r.AmountReserved - r.AmountConsumed - r.AmountReleased), token) ?? 0m;
        var accounts = context.DebtAccounts.AsNoTracking().Where(a => a.StoreId == store && a.FarmerProfileId == farmerId).Select(a => a.Id);
        var open = context.DebtEntries.AsNoTracking().Where(e => accounts.Contains(e.DebtAccountId) && e.OutstandingAmount > 0);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var overdue = await open.Where(e => e.DueDate < today).SumAsync(e => (decimal?)e.OutstandingAmount, token) ?? 0;
        var enabled = p.Status == FarmerCreditProfileStatus.Active
            && await context.FarmerProfiles.AnyAsync(f => f.Id == farmerId && f.User.Status == AgriSage.Domain.Features.Identity.Enums.UserStatus.Active, token)
            && await context.DebtAccounts.AnyAsync(a => a.StoreId == store && a.FarmerProfileId == farmerId
                && a.Status == AgriSage.Domain.Features.Debt.Enums.DebtAccountStatus.Active, token);
        var paid = await context.DebtTransactions.AsNoTracking().Where(t => accounts.Contains(t.DebtAccountId)
            && t.TransactionType == AgriSage.Domain.Features.Debt.Enums.DebtTransactionType.Payment
            && t.Status == AgriSage.Domain.Features.Debt.Enums.DebtTransactionStatus.Posted).SumAsync(t => (decimal?)-t.AmountDelta, token) ?? 0;
        return new CreditSummaryResponse(p.Id, farmerId, EnumText.Format(p.Status), p.CreditTier is { } tier
            ? new CustomerReference(tier.Id, tier.Code, tier.Name) : null, p.CreditLimit, p.CreditTier?.DefaultPaymentTermDays,
            outstanding, reserved, Math.Max(0, p.CalculateAvailableCredit(outstanding, reserved)), p.ApprovedBy, p.ApprovedAt, p.Note, p.Version,
            enabled, overdue, overdue > 0, paid, await open.CountAsync(token), await open.Select(e => (DateOnly?)e.DueDate).MinAsync(token));
    }

    public Task<CreditSummaryResponse> CreateAsync(Guid farmerId, CreateCustomerCreditRequest request, CancellationToken token) =>
        MutateAsync(farmerId, async (store, ct) =>
        {
            await writes.CreateCreditAsync(farmerId, store, request.CreditTierId, request.CreditLimit, request.Note, ct);
        }, false, "CREDIT.CREATE", token);

    public Task<CreditSummaryResponse> LimitAsync(Guid farmerId, CustomerCreditLimitRequest request, CancellationToken token) =>
        MutateAsync(farmerId, async (store, ct) =>
        {
            var p = await FindProfileAsync(farmerId, store, ct);
            var tier = request.CreditTierId ?? p.CreditTierId;
            if (tier != p.CreditTierId && tier is { } tierId) await writes.RequireTierAsync(tierId, store, ct);
            if (request.CreditLimit != p.CreditLimit || tier != p.CreditTierId)
                writes.ChangeLimit(p, request.CreditLimit, tier, request.Reason);
        }, false, "CREDIT.UPDATE", token);

    public Task<CreditSummaryResponse> StatusAsync(Guid farmerId, string status, CustomerCreditStatusRequest request, CancellationToken token) =>
        MutateAsync(farmerId, async (store, ct) =>
        {
            var p = await FindProfileAsync(farmerId, store, ct);
            if (!EnumText.TryParse<FarmerCreditProfileStatus>(status, out var parsed)) throw new BusinessRuleException("Invalid credit status.");
            await writes.SetCreditStatusAsync(p, parsed, request.Reason, ct);
        }, true, status switch
        {
            "ACTIVE" => "CREDIT.ACTIVATE", "SUSPENDED" => "CREDIT.SUSPEND", "BLOCKED" => "CREDIT.BLOCK",
            _ => throw new BusinessRuleException("Invalid credit status.")
        }, token);

    public async Task<IReadOnlyList<CreditLimitHistoryResponse>> HistoryAsync(Guid farmerId, CancellationToken token)
    {
        var profile = await GetAsync(farmerId, token);
        var rows = await context.CreditLimitHistories.AsNoTracking().Where(h => h.FarmerCreditProfileId == profile.ProfileId)
            .OrderByDescending(h => h.ChangedAt).ThenBy(h => h.Id).ToListAsync(token);
        var ids = rows.SelectMany(h => new[] { h.OldCreditTierId, h.NewCreditTierId }).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var tiers = await context.CreditTiers.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new CustomerReference(t.Id, t.Code, t.Name)).ToDictionaryAsync(t => t.Id, token);
        return rows.Select(h => new CreditLimitHistoryResponse(h.Id,
            h.OldCreditTierId is { } old ? tiers.GetValueOrDefault(old) : null, h.NewCreditTierId is { } next ? tiers.GetValueOrDefault(next) : null,
            h.OldCreditLimit, h.NewCreditLimit, h.Reason, h.ChangedBy, h.ChangedAt)).ToList();
    }

    public async Task<IReadOnlyList<CreditReservationResponse>> ReservationsAsync(Guid farmerId, bool activeOnly, CancellationToken token)
    {
        var p = await GetAsync(farmerId, token);
        var query = context.CreditReservations.AsNoTracking().Where(r => r.FarmerCreditProfileId == p.ProfileId);
        if (activeOnly) query = query.Where(r => r.AmountReserved > r.AmountConsumed + r.AmountReleased
            && (r.Status == CreditReservationStatus.Active || r.Status == CreditReservationStatus.PartiallyConsumed));
        var rows = await (from r in query
            join o in context.Orders.AsNoTracking() on r.OrderId equals o.Id
            where o.StoreId == r.StoreId
            orderby r.CreatedAt descending, r.Id
            select new { r.Id, r.OrderId, o.OrderNumber, r.AmountReserved, r.AmountConsumed, r.AmountReleased, r.Status, r.CreatedAt }).ToListAsync(token);
        return rows.Select(r => new CreditReservationResponse(r.Id, r.OrderId, r.OrderNumber, r.AmountReserved, r.AmountConsumed,
            r.AmountReleased, r.AmountReserved - r.AmountConsumed - r.AmountReleased, EnumText.Format(r.Status), r.CreatedAt)).ToList();
    }

    public async Task<PagedResult<CreditTierResponse>> TiersAsync(CustomerGroupListRequest request, CancellationToken token)
    {
        writes.Actor();
        var store = await ActiveStore.GetIdAsync(context, token);
        var query = context.CreditTiers.AsNoTracking().Where(t => t.StoreId == store);
        if (request.IsActive is { } active) query = query.Where(t => t.IsActive == active);
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            query = query.Where(t => t.Code.ToLower().Contains(term) || t.Name.ToLower().Contains(term));
        }
        var count = await query.LongCountAsync(token);
        var page = await query.OrderBy(t => t.Name).ThenBy(t => t.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return new PagedResult<CreditTierResponse>(await TierResponsesAsync(page, token), request.Page, request.PageSize, count);
    }

    public async Task<CreditTierResponse> TierAsync(Guid id, CancellationToken token)
    {
        writes.Actor();
        var store = await ActiveStore.GetIdAsync(context, token);
        var tier = await context.CreditTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.StoreId == store, token)
            ?? throw new NotFoundException("Credit tier", id);
        return (await TierResponsesAsync([tier], token))[0];
    }

    public async Task<CreditTierResponse> CreateTierAsync(CreditTierRequest request, CancellationToken token)
    {
        await writes.ActorAsync("CREDIT_TIERS.CREATE", token, manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var code = request.Code.Trim();
        if (await context.CreditTiers.IgnoreQueryFilters().AnyAsync(t => t.StoreId == store && t.Code.ToLower() == code.ToLower(), token))
            throw new ConflictException("Credit tier code already exists.");
        var tier = new CreditTier(store, code, request.Name.Trim(), request.DefaultCreditLimit, request.DefaultPaymentTermDays, Texts.Clean(request.Description));
        context.CreditTiers.Add(tier);
        audit.Record("CREDIT_TIER_CREATED", "CREDIT_TIER", tier.Id, store, newValues: Snapshot(tier));
        await SaveAsync(token);
        await tx.CommitAsync(token);
        return await TierAsync(tier.Id, token);
    }

    public Task<CreditTierResponse> UpdateTierAsync(Guid id, UpdateCreditTierRequest request, CancellationToken token) =>
        MutateTierAsync(id, (tier, _) =>
        {
            tier.Update(request.Name.Trim(), Texts.Clean(request.Description), request.DefaultCreditLimit, request.DefaultPaymentTermDays);
            return Task.CompletedTask;
        }, "CREDIT_TIERS.UPDATE", token);

    public Task<CreditTierResponse> SetTierActiveAsync(Guid id, bool active, CancellationToken token) =>
        MutateTierAsync(id, async (tier, ct) =>
        {
            if (active) { tier.Activate(); return; }
            if (await context.CustomerGroups.AnyAsync(g => g.DefaultCreditTierId == id, ct))
                throw new BusinessRuleException("Unlink this tier from customer groups before deactivating it.");
            tier.Deactivate();
        }, active ? "CREDIT_TIERS.ACTIVATE" : "CREDIT_TIERS.DEACTIVATE", token);

    private async Task<CreditSummaryResponse> MutateAsync(Guid farmerId, Func<Guid, CancellationToken, Task> change, bool manage, string permission, CancellationToken token)
    {
        await writes.ActorAsync(permission, token, manage);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        await locks.LockFarmerProfileAsync(farmerId, token);
        await writes.FindAsync(farmerId, token);
        await change(store, token);
        await SaveAsync(token);
        await tx.CommitAsync(token);
        return await GetAsync(farmerId, token);
    }

    private async Task<CreditTierResponse> MutateTierAsync(Guid id, Func<CreditTier, CancellationToken, Task> change, string permission, CancellationToken token)
    {
        await writes.ActorAsync(permission, token, manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var tier = await context.CreditTiers.FirstOrDefaultAsync(t => t.Id == id && t.StoreId == store, token)
            ?? throw new NotFoundException("Credit tier", id);
        var before = Snapshot(tier);
        await change(tier, token);
        audit.Record("CREDIT_TIER_UPDATED", "CREDIT_TIER", id, store, before, Snapshot(tier));
        await SaveAsync(token);
        await tx.CommitAsync(token);
        return await TierAsync(id, token);
    }

    private async Task<FarmerCreditProfile> FindProfileAsync(Guid farmerId, Guid store, CancellationToken token) =>
        await context.FarmerCreditProfiles.FirstOrDefaultAsync(p => p.FarmerProfileId == farmerId && p.StoreId == store, token)
        ?? throw new NotFoundException("Customer credit profile", farmerId);

    private async Task<IReadOnlyList<CreditTierResponse>> TierResponsesAsync(IReadOnlyList<CreditTier> tiers, CancellationToken token)
    {
        var ids = tiers.Select(t => t.Id).ToList();
        var counts = await context.FarmerCreditProfiles.AsNoTracking().Where(p => p.CreditTierId != null && ids.Contains(p.CreditTierId.Value))
            .GroupBy(p => p.CreditTierId!.Value).Select(g => new { Id = g.Key, Count = g.LongCount() }).ToDictionaryAsync(g => g.Id, g => g.Count, token);
        var groups = await context.CustomerGroups.AsNoTracking().Where(g => g.DefaultCreditTierId != null && ids.Contains(g.DefaultCreditTierId.Value))
            .Select(g => new { Tier = g.DefaultCreditTierId!.Value, Reference = new CustomerReference(g.Id, g.Code, g.Name) }).ToListAsync(token);
        return tiers.Select(t => new CreditTierResponse(t.Id, t.Code, t.Name, t.Description, t.DefaultCreditLimit,
            t.DefaultPaymentTermDays, t.IsActive, counts.GetValueOrDefault(t.Id), groups.Where(g => g.Tier == t.Id).Select(g => g.Reference).ToList())).ToList();
    }

    private async Task SaveAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException e) when (databaseErrors.IsUniqueViolation(e))
        { throw new ConflictException("Credit tier or customer credit profile already exists."); }
    }
    private static object Snapshot(CreditTier t) => new { t.Code, t.Name, t.DefaultCreditLimit, t.DefaultPaymentTermDays, t.IsActive };
}
