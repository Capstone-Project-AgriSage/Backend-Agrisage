using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Stocktakes;

public sealed class StocktakeService(IAgriSageDbContext context, IRowLockService locks, ICurrentUserService currentUser,
    IDateTimeProvider clock, AuditTrail audit, StocktakeQueries queries, StockAdjustmentPosting posting, AgriSage.Application.Features.Permissions.IPermissionEvaluator? permissions = null) : IStocktakeService
{
    public Task<PagedResult<StocktakeListItem>> ListAsync(StocktakeListRequest request, CancellationToken token) => queries.ListAsync(request, token);
    public Task<StocktakeResponse> GetAsync(Guid id, StocktakeDetailRequest request, CancellationToken token) => queries.GetAsync(id, request, token);

    public async Task<StocktakeResponse> CreateAsync(CreateStocktakeRequest request, CancellationToken token)
    {
        var actor = InventoryActors.Require(currentUser);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        var scopedLots = context.InventoryLots.AsNoTracking().Where(l => l.StoreProduct.StoreId == storeId);
        if (request.StoreProductIds is { Count: > 0 } products)
        {
            var found = await context.StoreProducts.Where(sp => sp.StoreId == storeId && products.Contains(sp.Id)).Select(sp => sp.Id).ToListAsync(token);
            foreach (var missing in products.Except(found))
            {
                throw new NotFoundException("Store product", missing);
            }
            scopedLots = scopedLots.Where(l => products.Contains(l.StoreProductId));
        }

        var lotIds = await scopedLots.Select(l => l.Id).ToArrayAsync(token);
        await locks.ShareLockLotBalancesAsync(lotIds, token);
        var snapshots = await scopedLots.Where(l => lotIds.Contains(l.Id)).Select(l => new
        {
            l.Id,
            l.Balance.QuantityOnHand,
            l.Balance.TotalCostValue
        }).ToListAsync(token);
        var stocktake = new Stocktake(storeId,
            await DocumentNumbers.NextAsync(context.Stocktakes.IgnoreQueryFilters().Where(s => s.StoreId == storeId)
                .Select(s => s.StocktakeNumber), DocumentNumbers.Stocktake, BusinessCalendar.Today(clock.UtcNow), token), actor, Texts.Clean(request.Note));
        foreach (var lot in snapshots.Where(l => request.IncludeEmptyLots || l.QuantityOnHand > 0).OrderBy(l => l.Id))
        {
            stocktake.AddItem(lot.Id, lot.QuantityOnHand, clock.UtcNow,
                lot.QuantityOnHand > 0 ? AgriSage.Domain.Common.CostRounding.RoundUnitCost(lot.TotalCostValue / lot.QuantityOnHand) : null);
        }
        context.Stocktakes.Add(stocktake);
        await context.SaveChangesAsync(token);
        var response = await queries.GetAsync(stocktake.Id, new(), token);
        await transaction.CommitAsync(token);
        return response;
    }

    public Task<StocktakeResponse> StartAsync(Guid id, CancellationToken token) => MutateAsync(id, false, (s, actor) =>
    {
        s.Start(actor, clock.UtcNow);
        return Task.CompletedTask;
    }, token);

    public Task<StocktakeResponse> CountAsync(Guid id, StocktakeCountsRequest request, CancellationToken token) =>
        MutateAsync(id, false, (s, actor) =>
        {
            RequireCounting(s);
            foreach (var count in request.Counts)
            {
                var item = s.Items.SingleOrDefault(i => i.Id == count.ItemId)
                    ?? throw new NotFoundException("Stocktake item", count.ItemId);
                if (count.CountedQuantity != item.SystemQuantitySnapshot && !StockAdjustmentReasons.IsStocktake(count.ReasonCode))
                {
                    throw new BusinessRuleException("A reason code is required for every stocktake difference.",
                        new Dictionary<string, string[]> { [item.InventoryLotId.ToString()] = ["A reason code is required."] });
                }
            }
            var now = clock.UtcNow;
            foreach (var count in request.Counts)
            {
                s.RecordCount(count.ItemId, count.CountedQuantity, actor, now,
                    Texts.Clean(count.ReasonCode)?.ToUpperInvariant(), Texts.Clean(count.Note), count.UnitCost);
            }
            return Task.CompletedTask;
        }, token);

    public Task<StocktakeResponse> RefreshStaleAsync(Guid id, CancellationToken token) => MutateAsync(id, false, async (s, _) =>
    {
        RequireCounting(s);
        await locks.ShareLockLotBalancesAsync(s.Items.Select(i => i.InventoryLotId).ToArray(), token);
        var stale = (await queries.StaleItemIdsAsync(id, token)).ToHashSet();
        var items = s.Items.Where(i => stale.Contains(i.Id)).ToList();
        var ids = items.Select(i => i.InventoryLotId).ToArray();
        var lots = await context.InventoryLots.AsNoTracking().Include(l => l.Balance)
            .Where(l => ids.Contains(l.Id) && l.StoreProduct.StoreId == s.StoreId).ToDictionaryAsync(l => l.Id, token);
        foreach (var item in items)
        {
            if (!lots.TryGetValue(item.InventoryLotId, out var lot))
            {
                throw new BusinessRuleException($"Lot {item.InventoryLotId} is no longer available for counting.");
            }
            s.RefreshItem(item.Id, lot.Balance.QuantityOnHand, clock.UtcNow, lot.Balance.AverageUnitCost);
        }
    }, token);

    public Task<StocktakeResponse> CompleteAsync(Guid id, CancellationToken token) => MutateAsync(id, true, async (s, actor) =>
    {
        RequireCounting(s);
        var ids = s.Items.Select(i => i.InventoryLotId).ToArray();
        await locks.LockLotBalancesAsync(ids, token);
        var stale = (await queries.StaleItemIdsAsync(id, token)).ToHashSet();
        var errors = new Dictionary<string, string[]>();
        foreach (var item in s.Items)
        {
            if (!item.IsCounted)
                errors[item.InventoryLotId.ToString()] = ["This lot has not been counted."];
            else if (stale.Contains(item.Id))
                errors[item.InventoryLotId.ToString()] = ["The snapshot is stale. Refresh and recount this lot."];
            else if (item.DifferenceQuantity > 0 && item.UnitCostSnapshot is null)
                errors[item.InventoryLotId.ToString()] = ["Enter a unit cost for this positive difference."];
            else if (item.DifferenceQuantity != 0 && !StockAdjustmentReasons.IsStocktake(item.ReasonCode))
                errors[item.InventoryLotId.ToString()] = ["A reason code is required for this difference."];
        }
        if (errors.Count > 0)
        {
            throw new BusinessRuleException("The stocktake cannot be completed; correct the listed lots.", errors);
        }
        if (s.Items.Any(i => i.CountedBy == actor))
        {
            throw new BusinessRuleException("The person approving the stocktake must be different from every person who counted it.");
        }

        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => ids.Contains(l.Id) && l.StoreProduct.StoreId == s.StoreId).ToDictionaryAsync(l => l.Id, token);
        foreach (var missing in ids.Except(lots.Keys))
        {
            throw new BusinessRuleException($"Lot {missing} is no longer available for adjustment.");
        }
        var lines = s.Items.Select(i => new StockAdjustmentPosting.Line(lots[i.InventoryLotId], i.DifferenceQuantity!.Value,
            i.UnitCostSnapshot, i.Note)).ToList();
        await posting.PostAsync(s.StoreId, actor, lines, "STOCKTAKE_DIFFERENCE", s.Note, s.Id, token);
        var before = new { s.StocktakeNumber, status = EnumText.Format(s.Status),
            quantities = s.Items.OrderBy(i => i.Id).Select(i => new { i.InventoryLotId,
                quantityOnHand = i.SystemQuantitySnapshot }).ToArray() };
        s.Complete(actor, clock.UtcNow);
        audit.Record("STOCKTAKE_COMPLETED", "STOCKTAKE", s.Id, s.StoreId, before,
            new { s.StocktakeNumber, status = EnumText.Format(s.Status),
                quantities = s.Items.OrderBy(i => i.Id).Select(i => new { i.InventoryLotId,
                    quantityOnHand = i.CountedQuantity, i.DifferenceQuantity, i.ReasonCode, i.Note }).ToArray() }, s.Note);
    }, token);

    public Task<StocktakeResponse> CancelAsync(Guid id, CancelStocktakeRequest request, CancellationToken token) =>
        MutateAsync(id, false, (s, _) =>
        {
            s.Cancel();
            audit.Record("STOCKTAKE_CANCELLED", "STOCKTAKE", s.Id, s.StoreId, reason: Texts.Clean(request.Reason));
            return Task.CompletedTask;
        }, token);

    public async Task DeleteAsync(Guid id, CancellationToken token)
    {
        var actor = InventoryActors.Require(currentUser);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStocktakeAsync(id, token);
        var s = await LoadAsync(id, storeId, token);
        s.MarkDeleted(actor, clock.UtcNow);
        audit.Record("STOCKTAKE_DELETED", "STOCKTAKE", id, storeId);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private async Task<StocktakeResponse> MutateAsync(Guid id, bool manage, Func<Stocktake, Guid, Task> change, CancellationToken token)
    {
        var actor = manage ? await InventoryActors.RequireManagerAsync(currentUser, permissions, "STOCKTAKES.COMPLETE", token) : InventoryActors.Require(currentUser);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockStocktakeAsync(id, token);
        var stocktake = await LoadAsync(id, storeId, token);
        await change(stocktake, actor);
        await context.SaveChangesAsync(token);
        var response = await queries.GetAsync(id, new(), token);
        await transaction.CommitAsync(token);
        return response;
    }

    private async Task<Stocktake> LoadAsync(Guid id, Guid storeId, CancellationToken token) =>
        await context.Stocktakes.Include(s => s.Items).SingleOrDefaultAsync(s => s.Id == id && s.StoreId == storeId, token)
        ?? throw new NotFoundException("Stocktake", id);

    private static void RequireCounting(Stocktake stocktake)
    {
        if (stocktake.Status != StocktakeStatus.InProgress)
        {
            throw new BusinessRuleException("Only an IN_PROGRESS stocktake can be counted, refreshed or completed.");
        }
    }
}
