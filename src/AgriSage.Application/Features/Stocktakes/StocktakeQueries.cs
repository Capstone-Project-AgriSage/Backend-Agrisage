using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Stocktakes;

public sealed class StocktakeQueries(IAgriSageDbContext context)
{
    public async Task<PagedResult<StocktakeListItem>> ListAsync(StocktakeListRequest request, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var query = context.Stocktakes.AsNoTracking().Where(s => s.StoreId == storeId);
        if (EnumText.TryParse<StocktakeStatus>(request.Status, out var status))
        {
            query = query.Where(s => s.Status == status);
        }
        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(s => s.CreatedAt >= start);
        }
        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(s => s.CreatedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(s => s.StocktakeNumber.ToLower().Contains(term) || (s.Note != null && s.Note.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(token);
        var rows = await query.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(s => new
            {
                s.Id,
                s.StocktakeNumber,
                s.Status,
                s.CreatedBy,
                s.CreatedAt,
                s.CompletedAt,
                Lines = s.Items.Count,
                Counted = s.Items.Count(i => i.CountedQuantity != null),
                Differences = s.Items.Count(i => i.DifferenceQuantity != null && i.DifferenceQuantity != 0)
            }).ToListAsync(token);
        return new(rows.Select(s => new StocktakeListItem(s.Id, s.StocktakeNumber, EnumText.Format(s.Status),
            s.Lines, s.Counted, s.Differences, s.CreatedBy, s.CreatedAt, s.CompletedAt)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<StocktakeResponse> GetAsync(Guid id, StocktakeDetailRequest request, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var header = await context.Stocktakes.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && s.StoreId == storeId, token)
            ?? throw new NotFoundException("Stocktake", id);
        var allItems = context.StocktakeItems.AsNoTracking().Where(i => i.StocktakeId == id);
        var totals = await allItems.GroupBy(i => i.StocktakeId)
            .Select(g => new StocktakeTotals(g.Count(), g.Count(i => i.CountedQuantity != null),
                g.Count(i => i.DifferenceQuantity != null && i.DifferenceQuantity != 0), g.Sum(i => i.DifferenceCostValue ?? 0)))
            .SingleOrDefaultAsync(token) ?? new(0, 0, 0, 0);
        var stale = (await StaleItemIdsAsync(id, token)).ToHashSet();
        var filtered = allItems;
        if (request.OnlyDifferences)
        {
            filtered = filtered.Where(i => i.DifferenceQuantity != null && i.DifferenceQuantity != 0);
        }
        if (request.OnlyUncounted)
        {
            filtered = filtered.Where(i => i.CountedQuantity == null);
        }

        // Historical lot/product labels remain visible after a catalog soft delete. Keep the item filter explicit
        // because IgnoreQueryFilters applies to the whole query, including the stocktake item source.
        var rows = await (from i in filtered
                          where i.DeletedAt == null
                          join lot in context.InventoryLots.IgnoreQueryFilters().AsNoTracking() on i.InventoryLotId equals lot.Id
                          orderby lot.StoreProduct.Product.Sku, lot.LotNumber, i.Id
                          select new
                          {
                              Item = i,
                              Sku = lot.StoreProduct.StoreSku ?? lot.StoreProduct.Product.Sku,
                              ProductName = lot.StoreProduct.Product.Name,
                              lot.LotNumber,
                              lot.ExpiryDate
                          }).ToListAsync(token);
        var items = rows.Select(r => new StocktakeItemResponse(r.Item.Id, r.Item.InventoryLotId, r.Sku, r.ProductName,
            r.LotNumber, r.ExpiryDate, r.Item.SystemQuantitySnapshot, r.Item.SnapshotAt, r.Item.CountedQuantity,
            r.Item.DifferenceQuantity, r.Item.UnitCostSnapshot, r.Item.DifferenceCostValue, r.Item.ReasonCode,
            r.Item.Note, r.Item.CountedBy, r.Item.CountedAt, stale.Contains(r.Item.Id))).ToList();
        var movements = await context.StockMovements.AsNoTracking().Where(m => m.StocktakeId == id)
            .OrderBy(m => m.MovementNumber).Select(m => new { m.Id, m.MovementNumber, m.MovementType }).ToListAsync(token);
        return new(header.Id, header.StocktakeNumber, EnumText.Format(header.Status), header.Note, header.CreatedBy,
            header.CreatedAt, header.StartedBy, header.StartedAt, header.CompletedBy, header.CompletedAt, totals,
            movements.Select(m => new StocktakeMovement(m.Id, m.MovementNumber, EnumText.Format(m.MovementType))).ToList(), items);
    }

    public Task<List<Guid>> StaleItemIdsAsync(Guid id, CancellationToken token) =>
        context.StocktakeItems.AsNoTracking().Where(i => i.StocktakeId == id && i.CountedAt != null
            && context.StockMovementItems.Any(mi => mi.InventoryLotId == i.InventoryLotId
                && context.StockMovements.Any(m => m.Id == mi.StockMovementId && m.Status == StockMovementStatus.Posted
                    && m.PostedAt > i.SnapshotAt.AddMinutes(-5) && m.PostedAt <= i.CountedAt)))
            .Select(i => i.Id).ToListAsync(token);
}
