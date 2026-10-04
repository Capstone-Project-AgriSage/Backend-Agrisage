using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Pricing.Entities;
using AgriSage.Domain.Features.Pricing.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Customers;

public sealed class CustomerGroupService(IAgriSageDbContext context, IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors, IRowLockService locks, ICustomerGroupDefaultSwitcher defaults,
    CustomerWrites writes, AuditTrail audit) : ICustomerGroupService
{
    public async Task<PagedResult<CustomerGroupResponse>> ListAsync(CustomerGroupListRequest request, CancellationToken token)
    {
        writes.Actor();
        var store = await ActiveStore.GetIdAsync(context, token);
        var query = context.CustomerGroups.AsNoTracking().Where(g => g.StoreId == store);
        if (request.IsActive is { } active) query = query.Where(g => g.IsActive == active);
        if (Texts.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            query = query.Where(g => g.Code.ToLower().Contains(term) || g.Name.ToLower().Contains(term));
        }
        var count = await query.LongCountAsync(token);
        var groups = await query.OrderBy(g => g.Priority).ThenBy(g => g.Name).ThenBy(g => g.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return new PagedResult<CustomerGroupResponse>(await ResponsesAsync(groups, token), request.Page, request.PageSize, count);
    }

    public async Task<CustomerGroupResponse> GetAsync(Guid id, CancellationToken token)
    {
        writes.Actor();
        var store = await ActiveStore.GetIdAsync(context, token);
        var group = await context.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id && g.StoreId == store, token)
            ?? throw new NotFoundException("Customer group", id);
        return (await ResponsesAsync([group], token))[0];
    }

    public async Task<CustomerGroupResponse> CreateAsync(CustomerGroupRequest request, CancellationToken token)
    {
        writes.Actor(manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var code = request.Code.Trim();
        if (await context.CustomerGroups.IgnoreQueryFilters().AnyAsync(g => g.StoreId == store && g.Code.ToLower() == code.ToLower(), token))
            throw new ConflictException("Customer group code already exists.");
        var group = new CustomerGroup(store, code, request.Name.Trim(), Texts.Clean(request.Description), request.Priority);
        context.CustomerGroups.Add(group);
        Record("CUSTOMER_GROUP_CREATED", group);
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(group.Id, token);
    }

    public Task<CustomerGroupResponse> UpdateAsync(Guid id, UpdateCustomerGroupRequest request, CancellationToken token) =>
        MutateAsync(id, (g, _) =>
        {
            g.Update(request.Name.Trim(), Texts.Clean(request.Description), request.Priority);
            return Task.CompletedTask;
        }, "CUSTOMER_GROUP_UPDATED", token);

    public Task<CustomerGroupResponse> SetActiveAsync(Guid id, bool active, CancellationToken token) =>
        MutateAsync(id, async (g, ct) =>
        {
            if (active) { g.Activate(); return; }
            if (g.IsDefault) throw new BusinessRuleException("The default customer group cannot be deactivated.");
            if (await context.CustomerGroupAssignments.AnyAsync(a => a.CustomerGroupId == id && a.EffectiveTo == null, ct))
                throw new BusinessRuleException("Move the group's current customers before deactivating it.");
            g.Deactivate();
        }, "CUSTOMER_GROUP_STATUS_CHANGED", token);

    public async Task<CustomerGroupResponse> SetDefaultAsync(Guid id, CancellationToken token)
    {
        writes.Actor(manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var target = await context.CustomerGroups.FirstOrDefaultAsync(g => g.Id == id && g.StoreId == store, token)
            ?? throw new NotFoundException("Customer group", id);
        if (!target.IsActive) throw new BusinessRuleException("The default customer group must be active.");
        var old = await context.CustomerGroups.Where(g => g.StoreId == store && g.IsDefault && g.Id != id).ToListAsync(token);
        foreach (var group in old)
        {
            audit.Record("CUSTOMER_GROUP_DEFAULT_CHANGED", "CUSTOMER_GROUP", group.Id, store,
                new { IsDefault = true }, new { IsDefault = false });
            group.UnsetDefault();
        }
        audit.Record("CUSTOMER_GROUP_DEFAULT_CHANGED", "CUSTOMER_GROUP", id, store,
            new { target.IsDefault }, new { IsDefault = true });
        target.SetAsDefault();
        await defaults.ClearPreviousAsync(store, id, token);
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    public async Task DeleteAsync(Guid id, CancellationToken token)
    {
        writes.Actor(manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var group = await FindAsync(id, store, token);
        if (group.IsDefault) throw new BusinessRuleException("The default customer group cannot be deleted.");
        if (group.DefaultCreditTierId != null
            || await context.AuditLogs.AsNoTracking().AnyAsync(a => a.StoreId == store && a.EntityId == id
                && a.EntityType == "CUSTOMER_GROUP" && a.Action == "CUSTOMER_GROUP_CREDIT_TIER_CHANGED", token)
            || await context.CustomerGroupAssignments.IgnoreQueryFilters().AnyAsync(a => a.CustomerGroupId == id, token)
            || await context.CustomerGroupPriceLists.IgnoreQueryFilters().AnyAsync(a => a.CustomerGroupId == id, token)
            || await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.CustomerGroupIdSnapshot == id, token))
            throw new ConflictException("This customer group has been used; deactivate it instead.");
        Record("CUSTOMER_GROUP_DELETED", group);
        context.CustomerGroups.Remove(group);
        await SaveAsync(token);
        await transaction.CommitAsync(token);
    }

    public Task<CustomerGroupResponse> SetCreditTierAsync(Guid id, GroupCreditTierRequest request, CancellationToken token) =>
        MutateAsync(id, async (g, ct) =>
        {
            if (request.CreditTierId is { } tier) await writes.RequireTierAsync(tier, g.StoreId, ct);
            g.SetDefaultCreditTier(request.CreditTierId);
        }, "CUSTOMER_GROUP_CREDIT_TIER_CHANGED", token);

    public Task<CustomerGroupResponse> SetPriceListAsync(Guid id, GroupPriceListRequest request, CancellationToken token) =>
        MutateAsync(id, async (g, ct) =>
        {
            var list = await context.PriceLists.FirstOrDefaultAsync(p => p.Id == request.PriceListId && p.StoreId == g.StoreId, ct)
                ?? throw new NotFoundException("Price list", request.PriceListId);
            if (list.Status is not (PriceListStatus.Draft or PriceListStatus.Active))
                throw new BusinessRuleException("Only DRAFT or ACTIVE price lists can be linked.");
            var from = (request.EffectiveFrom ?? clock.UtcNow).ToUniversalTime();
            var current = await context.CustomerGroupPriceLists.FirstOrDefaultAsync(p => p.CustomerGroupId == id && p.EffectiveTo == null, ct);
            if (current?.PriceListId == request.PriceListId) return;
            if (current != null && current.EffectiveFrom >= from)
                throw new BusinessRuleException("A price-list link must start after the current link.");
            current?.End(from);
            context.CustomerGroupPriceLists.Add(new CustomerGroupPriceList(id, list.Id, from, writes.Actor(manage: true)));
        }, "CUSTOMER_GROUP_PRICE_LIST_CHANGED", token);

    public async Task<IReadOnlyList<GroupPriceListResponse>> PriceListsAsync(Guid id, CancellationToken token)
    {
        await GetAsync(id, token);
        var links = await context.CustomerGroupPriceLists.AsNoTracking().Where(p => p.CustomerGroupId == id)
            .OrderByDescending(p => p.EffectiveFrom).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.PriceListId, p.PriceList.Code, p.PriceList.Name, p.PriceList.Status, p.EffectiveFrom, p.EffectiveTo }).ToListAsync(token);
        return links.Select(p => new GroupPriceListResponse(p.Id, new CustomerReference(p.PriceListId, p.Code, p.Name),
            EnumText.Format(p.Status), p.EffectiveFrom, p.EffectiveTo)).ToList();
    }

    private async Task<CustomerGroupResponse> MutateAsync(Guid id, Func<CustomerGroup, CancellationToken, Task> change,
        string action, CancellationToken token)
    {
        writes.Actor(manage: true);
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStoreAsync(store, token);
        var group = await FindAsync(id, store, token);
        var before = Snapshot(group);
        await change(group, token);
        audit.Record(action, "CUSTOMER_GROUP", id, store, before, Snapshot(group));
        await SaveAsync(token);
        await transaction.CommitAsync(token);
        return await GetAsync(id, token);
    }

    private async Task<CustomerGroup> FindAsync(Guid id, Guid store, CancellationToken token) =>
        await context.CustomerGroups.FirstOrDefaultAsync(g => g.Id == id && g.StoreId == store, token)
        ?? throw new NotFoundException("Customer group", id);

    private async Task<IReadOnlyList<CustomerGroupResponse>> ResponsesAsync(IReadOnlyList<CustomerGroup> groups, CancellationToken token)
    {
        var ids = groups.Select(g => g.Id).ToList();
        var counts = await context.CustomerGroupAssignments.AsNoTracking().Where(a => ids.Contains(a.CustomerGroupId) && a.EffectiveTo == null)
            .GroupBy(a => a.CustomerGroupId).Select(g => new { Id = g.Key, Count = g.LongCount() }).ToDictionaryAsync(g => g.Id, g => g.Count, token);
        // Unassigned Farmers resolve to the default group, as they do in PriceResolver.
        var implicitMembers = groups.Any(g => g.IsDefault)
            ? await context.FarmerProfiles.AsNoTracking().LongCountAsync(f => f.User.Role.Code == AgriSage.Domain.Features.Identity.Enums.RoleCode.Farmer
                && !context.CustomerGroupAssignments.Any(a => a.FarmerProfileId == f.Id && a.EffectiveTo == null), token) : 0;
        var now = clock.UtcNow;
        var links = await context.CustomerGroupPriceLists.AsNoTracking()
            .Where(p => ids.Contains(p.CustomerGroupId) && p.EffectiveFrom <= now && (p.EffectiveTo == null || p.EffectiveTo > now))
            .Select(p => new { p.CustomerGroupId, Reference = new CustomerReference(p.PriceListId, p.PriceList.Code, p.PriceList.Name) })
            .ToDictionaryAsync(p => p.CustomerGroupId, p => p.Reference, token);
        var tierIds = groups.Where(g => g.DefaultCreditTierId != null).Select(g => g.DefaultCreditTierId!.Value).ToList();
        var tiers = await context.CreditTiers.AsNoTracking().Where(t => tierIds.Contains(t.Id))
            .Select(t => new CustomerReference(t.Id, t.Code, t.Name)).ToDictionaryAsync(t => t.Id, token);
        return groups.Select(g => new CustomerGroupResponse(g.Id, g.Code, g.Name, g.Description, g.Priority, g.IsDefault, g.IsActive,
            counts.GetValueOrDefault(g.Id) + (g.IsDefault ? implicitMembers : 0), links.GetValueOrDefault(g.Id),
            g.DefaultCreditTierId is { } tier ? tiers.GetValueOrDefault(tier) : null, g.CreatedAt)).ToList();
    }

    private void Record(string action, CustomerGroup group) =>
        audit.Record(action, "CUSTOMER_GROUP", group.Id, group.StoreId, newValues: Snapshot(group));

    private static object Snapshot(CustomerGroup g) => new { g.Code, g.Name, g.Description, g.Priority, g.IsActive, g.IsDefault, g.DefaultCreditTierId };

    private async Task SaveAsync(CancellationToken token)
    {
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException e) when (databaseErrors.IsUniqueViolation(e))
        {
            throw new ConflictException("Customer group code, default or price-list link conflicts with an existing group.");
        }
    }
}
