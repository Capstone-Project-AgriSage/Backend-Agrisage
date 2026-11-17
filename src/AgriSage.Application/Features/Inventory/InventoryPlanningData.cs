using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// Batch projections and SQL-side movement aggregation, shared by planning and risk/velocity reports.
public sealed class InventoryPlanningData(IAgriSageDbContext context, IDateTimeProvider clock)
{
    public sealed record Product(Guid Id, string Sku, string Name, string Unit, Guid CategoryId,
        bool Enabled, long? Minimum, long? Target, int Lead, long Safety, int Coverage,
        Guid? PackagingId, string? PurchaseUnit, long Multiple);
    public sealed record Lot(Guid Id, Guid ProductId, string? Number, DateOnly? Expiry, string Status,
        long OnHand, long Reserved, decimal Value);
    public sealed record SaleDay(Guid ProductId, DateOnly Day, long NetQuantity, long SaleQuantity);
    public sealed record Observation(Guid ProductId, DateTimeOffset FirstAt);
    public sealed record Snapshot(Guid StoreId, DateTimeOffset GeneratedAt, DateOnly Today,
        IReadOnlyList<Product> Products, IReadOnlyList<Lot> Lots, IReadOnlyList<SaleDay> Sales,
        IReadOnlyList<Observation> Observations);

    public async Task<Snapshot> LoadAsync(int historyDays, string? search, Guid? categoryId,
        Guid? productId, CancellationToken token, bool includeInactive = false)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var start = BusinessCalendar.StartOfDay(today.AddDays(-historyDays));
        var end = BusinessCalendar.StartOfDay(today);
        var query = context.StoreProducts.AsNoTracking().Where(p => p.StoreId == store);
        if (!includeInactive) query = query.Where(p => p.IsActive && p.IsSellable && p.Product.Status == ProductStatus.Active);
        if (categoryId.HasValue) query = query.Where(p => p.Product.CategoryId == categoryId);
        if (productId.HasValue) query = query.Where(p => p.Id == productId);
        if (!string.IsNullOrWhiteSpace(search)) {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Product.Name.ToLower().Contains(term) || p.Product.Sku.ToLower().Contains(term)
                || (p.StoreSku != null && p.StoreSku.ToLower().Contains(term)));
        }
        var products = await query.Select(p => new Product(p.Id, p.StoreSku ?? p.Product.Sku, p.Product.Name,
            p.Product.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Code).FirstOrDefault() ?? "",
            p.Product.CategoryId, p.ReplenishmentEnabled, p.MinStockLevelBase, p.TargetStockLevelBase,
            p.LeadTimeDays, p.SafetyStockBase, p.TargetCoverageDays,
            p.Product.Packagings.Where(u => u.IsPurchaseUnit && u.Status == "ACTIVE").OrderBy(u => u.ConversionToBase).ThenBy(u => u.Id).Select(u => (Guid?)u.Id).FirstOrDefault(),
            p.Product.Packagings.Where(u => u.IsPurchaseUnit && u.Status == "ACTIVE").OrderBy(u => u.ConversionToBase).ThenBy(u => u.Id).Select(u => u.PackagingName ?? u.Unit.Name).FirstOrDefault(),
            p.Product.Packagings.Where(u => u.IsPurchaseUnit && u.Status == "ACTIVE").OrderBy(u => u.ConversionToBase).ThenBy(u => u.Id).Select(u => (long?)u.ConversionToBase).FirstOrDefault() ?? 1))
            .ToListAsync(token);
        var ids = products.Select(p => p.Id).ToArray();
        var lots = await context.InventoryLots.AsNoTracking().Where(l => ids.Contains(l.StoreProductId))
            .Select(l => new Lot(l.Id, l.StoreProductId, l.LotNumber, l.ExpiryDate, EnumText.Format(l.Status),
                l.Balance.QuantityOnHand, l.Balance.QuantityReserved, l.Balance.TotalCostValue)).ToListAsync(token);
        var ledger = from m in context.StockMovements.AsNoTracking()
                     where m.StoreId == store && m.Status == StockMovementStatus.Posted
                     join i in context.StockMovementItems on m.Id equals i.StockMovementId
                     join l in context.InventoryLots on i.InventoryLotId equals l.Id
                     where ids.Contains(l.StoreProductId)
                     select new { ProductId = l.StoreProductId, m.PostedAt, m.MovementType, m.ReversalOfMovementId, i.QuantityDeltaBase };
        var observations = await ledger.GroupBy(x => x.ProductId)
            .Select(g => new Observation(g.Key, g.Min(x => x.PostedAt!.Value))).ToListAsync(token);
        var demand = from x in ledger
                     where x.PostedAt >= start && x.PostedAt < end
                     join original in context.StockMovements on x.ReversalOfMovementId equals original.Id into originals
                     from original in originals.DefaultIfEmpty()
                     where x.MovementType == StockMovementType.Sale || x.MovementType == StockMovementType.ReturnIn
                         || (x.MovementType == StockMovementType.Reversal && original != null
                             && (original.MovementType == StockMovementType.Sale || original.MovementType == StockMovementType.ReturnIn))
                     select new { x.ProductId, Day = DateOnly.FromDateTime(x.PostedAt!.Value.UtcDateTime.AddHours(7)),
                         Net = -x.QuantityDeltaBase, Sale = x.MovementType == StockMovementType.Sale ? -x.QuantityDeltaBase : 0 };
        var sales = await demand.GroupBy(x => new { x.ProductId, x.Day })
            .Select(g => new SaleDay(g.Key.ProductId, g.Key.Day, g.Sum(x => x.Net), g.Sum(x => x.Sale))).ToListAsync(token);
        return new(store, now, today, products, lots, sales, observations);
    }
}
