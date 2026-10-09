using AgriSage.Application.Common;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

public sealed partial class InventoryService
{
    public async Task<PagedResult<StockSummaryItem>> GetStockSummaryAsync(
        StockSummaryRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var query = SummaryQuery(storeId, today);

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(r => r.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(r => r.Sku.ToLower().Contains(term)
                || r.ProductSku.ToLower().Contains(term) || r.ProductName.ToLower().Contains(term));
        }

        if (request.LowStockOnly)
        {
            query = query.Where(r => r.MinStockLevelBase != null && r.Sellable < r.MinStockLevelBase);
        }

        if (request.HasStock is { } hasStock)
        {
            query = hasStock ? query.Where(r => r.OnHand > 0) : query.Where(r => r.OnHand == 0);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderBy(r => r.Sku).ThenBy(r => r.StoreProductId)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var items = rows.Select(r => new StockSummaryItem(
            r.StoreProductId, r.Sku, r.ProductName, r.BaseUnit, r.OnHand, r.Reserved, r.OnHand - r.Reserved,
            r.Sellable, r.StockValue, r.MinStockLevelBase,
            r.MinStockLevelBase is not null && r.Sellable < r.MinStockLevelBase,
            r.NearestExpiryDate, r.LotCount)).ToList();

        return new PagedResult<StockSummaryItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<PagedResult<InventoryAlertItem>> GetAlertsAsync(
        InventoryAlertsRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var lastDay = today.AddDays(request.WithinDays);
        var type = string.IsNullOrWhiteSpace(request.Type) ? null : request.Type.Trim().ToUpperInvariant();

        // The expiry alerts include held lots: staff still need to know what physically remains in stock.
        var lots = context.InventoryLots.AsNoTracking()
            .Where(l => l.StoreProduct.StoreId == storeId && l.Balance.QuantityOnHand > 0
                && l.ExpiryDate != null && l.ExpiryDate <= lastDay);
        var lotAlerts = lots.Select(l => new AlertRow
        {
            Type = l.ExpiryDate < today ? "EXPIRED" : "EXPIRING",
            StoreProductId = l.StoreProductId,
            Sku = l.StoreProduct.StoreSku ?? l.StoreProduct.Product.Sku,
            ProductName = l.StoreProduct.Product.Name,
            InventoryLotId = l.Id,
            LotNumber = l.LotNumber,
            ExpiryDate = l.ExpiryDate,
            LotStatus = l.Status,
            OnHand = l.Balance.QuantityOnHand,
            Reserved = l.Balance.QuantityReserved,
            MinStockLevelBase = l.StoreProduct.MinStockLevelBase
        });
        var lowStockAlerts = SummaryQuery(storeId, today)
            .Where(r => r.MinStockLevelBase != null && r.Sellable < r.MinStockLevelBase)
            .Select(r => new AlertRow
            {
                Type = "LOW_STOCK",
                StoreProductId = r.StoreProductId,
                Sku = r.Sku,
                ProductName = r.ProductName,
                InventoryLotId = null,
                LotNumber = null,
                ExpiryDate = null,
                LotStatus = null,
                OnHand = r.OnHand,
                Reserved = r.Reserved,
                MinStockLevelBase = r.MinStockLevelBase
            });

        var query = type switch
        {
            "LOW_STOCK" => lowStockAlerts,
            "EXPIRING" or "EXPIRED" => lotAlerts.Where(r => r.Type == type),
            _ => lotAlerts.Concat(lowStockAlerts)
        };
        var total = await query.LongCountAsync(cancellationToken);
        // All alert types share one count and one stable, SQL-side page.
        var rows = await query.OrderBy(r => r.Type).ThenBy(r => r.Sku).ThenBy(r => r.StoreProductId)
            .ThenBy(r => r.ExpiryDate).ThenBy(r => r.InventoryLotId)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var items = rows.Select(r => new InventoryAlertItem(
            r.Type, r.StoreProductId, r.Sku, r.ProductName, r.InventoryLotId, r.LotNumber, r.ExpiryDate,
            r.ExpiryDate is { } expiry ? expiry.DayNumber - today.DayNumber : null,
            r.LotStatus is { } status ? EnumText.Format(status) : null,
            r.OnHand, r.Reserved, r.MinStockLevelBase)).ToList();

        return new PagedResult<InventoryAlertItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<ExpireDueLotsResponse> ExpireDueLotsAsync(CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var today = BusinessCalendar.Today(clock.UtcNow);
        await using var transaction = await context.BeginTransactionAsync(cancellationToken);

        var due = context.InventoryLots.Where(l => l.StoreProduct.StoreId == storeId
            && l.Status == InventoryLotStatus.Active && l.ExpiryDate < today);
        var ids = await due.AsNoTracking().Select(l => l.Id).ToListAsync(cancellationToken);
        await locks.LockInventoryLotsAsync(ids, cancellationToken);
        // Re-evaluate after waiting: another call may have expired a lot or an operator may have blocked it.
        var lots = await due.Where(l => ids.Contains(l.Id)).OrderBy(l => l.Id).ToListAsync(cancellationToken);
        var expired = lots.Where(l => l.ExpireIfDue(today))
            .Select(l => new ExpiredLotItem(l.Id, l.LotNumber, l.ExpiryDate)).ToList();
        if (expired.Count > 0)
            audit?.Record("INVENTORY_LOTS_EXPIRED", "STORE", storeId, storeId,
                newValues: new { count = expired.Count, lotIds = expired.Select(l => l.Id).ToArray() });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ExpireDueLotsResponse(expired.Count, expired);
    }

    private IQueryable<SummaryRow> SummaryQuery(Guid storeId, DateOnly today)
    {
        var totals = context.InventoryLots.AsNoTracking()
            .Where(l => l.StoreProduct.StoreId == storeId)
            .GroupBy(l => l.StoreProductId)
            .Select(g => new
            {
                StoreProductId = g.Key,
                OnHand = g.Sum(l => l.Balance.QuantityOnHand),
                Reserved = g.Sum(l => l.Balance.QuantityReserved),
                Sellable = g.Sum(l => l.Status == InventoryLotStatus.Active
                    && (l.ExpiryDate == null || l.ExpiryDate >= today)
                    ? l.Balance.QuantityOnHand - l.Balance.QuantityReserved : 0),
                StockValue = g.Sum(l => l.Balance.TotalCostValue),
                NearestExpiryDate = g.Min(l => l.Status == InventoryLotStatus.Active
                    && (l.ExpiryDate == null || l.ExpiryDate >= today) && l.Balance.QuantityOnHand > 0
                    ? l.ExpiryDate : null),
                LotCount = g.Count()
            });

        // Start from store products so a product without any lot still has a zero-stock row and can alert.
        return from sp in context.StoreProducts.AsNoTracking()
               where sp.StoreId == storeId
               join total in totals on sp.Id equals total.StoreProductId into joined
               from total in joined.DefaultIfEmpty()
               select new SummaryRow
               {
                   StoreProductId = sp.Id,
                   CategoryId = sp.Product.CategoryId,
                   Sku = sp.StoreSku ?? sp.Product.Sku,
                   ProductSku = sp.Product.Sku,
                   ProductName = sp.Product.Name,
                   BaseUnit = sp.Product.Packagings.Where(p => p.IsBaseUnit).Select(p => p.Unit.Code).FirstOrDefault() ?? "",
                   MinStockLevelBase = sp.MinStockLevelBase,
                   OnHand = (long?)total.OnHand ?? 0,
                   Reserved = (long?)total.Reserved ?? 0,
                   Sellable = (long?)total.Sellable ?? 0,
                   StockValue = (decimal?)total.StockValue ?? 0,
                   NearestExpiryDate = total.NearestExpiryDate,
                   LotCount = (int?)total.LotCount ?? 0
               };
    }

    private sealed class SummaryRow
    {
        public Guid StoreProductId { get; init; }
        public Guid CategoryId { get; init; }
        public string Sku { get; init; } = null!;
        public string ProductSku { get; init; } = null!;
        public string ProductName { get; init; } = null!;
        public string BaseUnit { get; init; } = null!;
        public long OnHand { get; init; }
        public long Reserved { get; init; }
        public long Sellable { get; init; }
        public decimal StockValue { get; init; }
        public long? MinStockLevelBase { get; init; }
        public DateOnly? NearestExpiryDate { get; init; }
        public int LotCount { get; init; }
    }

    private sealed class AlertRow
    {
        public string Type { get; init; } = null!;
        public Guid StoreProductId { get; init; }
        public string Sku { get; init; } = null!;
        public string ProductName { get; init; } = null!;
        public Guid? InventoryLotId { get; init; }
        public string? LotNumber { get; init; }
        public DateOnly? ExpiryDate { get; init; }
        public InventoryLotStatus? LotStatus { get; init; }
        public long OnHand { get; init; }
        public long Reserved { get; init; }
        public long? MinStockLevelBase { get; init; }
    }
}
