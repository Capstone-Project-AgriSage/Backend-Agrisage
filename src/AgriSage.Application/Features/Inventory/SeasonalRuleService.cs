using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

public sealed class SeasonalRuleService(IAgriSageDbContext context, IDateTimeProvider clock,
    ICurrentUserService user, AuditTrail audit) : ISeasonalRuleService
{
    public async Task<SeasonalRulePage> ListAsync(SeasonalRuleListRequest request, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var query = context.SeasonalRules.AsNoTracking().Where(r => r.StoreId == store);
        if (request.IsActive is { } active) query = query.Where(r => r.IsActive == active);
        if (request.FromDate is { } from) query = query.Where(r => r.EndsOn >= from);
        if (request.ToDate is { } to) query = query.Where(r => r.StartsOn <= to);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Name.ToLower().Contains(term) || r.CropType.ToLower().Contains(term)
                || (r.Region != null && r.Region.ToLower().Contains(term)));
        }
        var stats = await query.GroupBy(r => 1).Select(g => new SeasonalRuleSummary(g.LongCount(),
            g.LongCount(r => r.IsActive), g.LongCount(r => r.StartsOn > today), g.LongCount(r => r.EndsOn < today)))
            .SingleOrDefaultAsync(token) ?? new(0, 0, 0, 0);
        var ids = await query.OrderByDescending(r => r.StartsOn).ThenBy(r => r.Id)
            .Skip(request.Skip).Take(request.PageSize).Select(r => r.Id).ToArrayAsync(token);
        var rows = await ReadAsync(store, ids, token);
        return new(ids.Select(id => rows.Single(r => r.Id == id)).ToList(), request.Page, request.PageSize, stats.Total, stats);
    }
    public async Task<SeasonalRuleResponse> GetAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        return (await ReadAsync(store, [id], token)).SingleOrDefault() ?? throw new NotFoundException("Seasonal rule", id);
    }
    public async Task<SeasonalRuleResponse> SaveAsync(Guid? id, SeasonalRuleWriteRequest request, CancellationToken token)
    {
        var actor = user.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var store = await ActiveStore.GetIdAsync(context, token);
        await using var tx = await context.BeginTransactionAsync(token);
        var products = request.Items.Select(i => i.StoreProductId).ToArray();
        if (await context.StoreProducts.CountAsync(p => p.StoreId == store && products.Contains(p.Id), token) != products.Length)
            throw new BusinessRuleException("Every seasonal product must belong to the current store.");
        SeasonalRule rule;
        if (id is { } key) {
            rule = await context.SeasonalRules.Include(r => r.Items).SingleOrDefaultAsync(r => r.StoreId == store && r.Id == key, token)
                ?? throw new NotFoundException("Seasonal rule", key);
            if (request.Version is null || request.Version != rule.Version)
                throw new ConflictException("The seasonal rule changed. Reload it before saving.");
            rule.Update(request.Name, request.CropType, request.Region, request.StartsOn, request.EndsOn,
                request.PrepareDays, request.DemandFactor, request.IsActive);
            // Child-only edits must participate in root optimistic concurrency too.
            context.SeasonalRules.Entry(rule).Property(r => r.Name).IsModified = true;
        } else {
            rule = new(store, request.Name, request.CropType, request.Region, request.StartsOn, request.EndsOn,
                request.PrepareDays, request.DemandFactor);
            if (!request.IsActive) rule.Deactivate();
            context.SeasonalRules.Add(rule);
        }
        rule.SetItems(request.Items.Select(i => (i.StoreProductId, i.TargetStockLevelBase)).ToArray(), actor, clock.UtcNow);
        audit.Record(id is null ? "SEASONAL_RULE_CREATED" : "SEASONAL_RULE_UPDATED", "SEASONAL_RULE", rule.Id,
            store, newValues: new { rule.Name, rule.StartsOn, rule.EndsOn, rule.DemandFactor, request.Items, rule.IsActive });
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return await GetAsync(rule.Id, token);
    }
    public async Task<SeasonalRuleResponse> DeactivateAsync(Guid id, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var rule = await context.SeasonalRules.SingleOrDefaultAsync(r => r.Id == id && r.StoreId == store, token)
            ?? throw new NotFoundException("Seasonal rule", id);
        rule.Deactivate();
        audit.Record("SEASONAL_RULE_DEACTIVATED", "SEASONAL_RULE", id, store);
        await context.SaveChangesAsync(token);
        return await GetAsync(id, token);
    }
    private async Task<List<SeasonalRuleResponse>> ReadAsync(Guid store, Guid[] ids, CancellationToken token)
    {
        return await context.SeasonalRules.AsNoTracking().Where(r => r.StoreId == store && ids.Contains(r.Id))
            .Select(r => new SeasonalRuleResponse(r.Id, r.Name, r.CropType, r.Region, r.StartsOn, r.EndsOn,
                r.PrepareDays, r.DemandFactor, r.IsActive, r.Version,
                (from i in context.SeasonalRuleItems where i.SeasonalRuleId == r.Id
                 join p in context.StoreProducts on i.StoreProductId equals p.Id
                 orderby p.Product.Name
                 select new SeasonalRuleProduct(p.Id, p.StoreSku ?? p.Product.Sku, p.Product.Name, i.TargetStockLevelBase)).ToList()))
            .ToListAsync(token);
    }
}
