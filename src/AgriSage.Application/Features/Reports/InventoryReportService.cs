using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

public sealed class InventoryReportService(IAgriSageDbContext context, IDateTimeProvider clock) : IInventoryReportService
{
    private sealed class ProductInfo
    {
        public Guid Id { get; init; }
        public string Sku { get; init; } = null!;
        public string Name { get; init; } = null!;
        public string Unit { get; init; } = null!;
        public Guid CategoryId { get; init; }
        public string Category { get; init; } = null!;
    }
    private sealed class LedgerEntry
    {
        public Guid ProductId { get; init; }
        public Guid LotId { get; init; }
        public string? LotNumber { get; init; }
        public Guid ItemId { get; init; }
        public Guid MovementId { get; init; }
        public string Number { get; init; } = null!;
        public StockMovementType Type { get; init; }
        public DateTimeOffset PostedAt { get; init; }
        public long Quantity { get; init; }
        public decimal Cost { get; init; }
        public decimal UnitCost { get; init; }
        public Guid? ReceiptId { get; init; }
        public Guid? OrderId { get; init; }
        public Guid? DeliveryId { get; init; }
        public Guid? StocktakeId { get; init; }
        public Guid? ReturnId { get; init; }
    }

    private IQueryable<ProductInfo> Products(Guid storeId, Guid? categoryId = null) =>
        context.StoreProducts.AsNoTracking().Where(p => p.StoreId == storeId
            && (categoryId == null || p.Product.CategoryId == categoryId))
        .Select(p => new ProductInfo
        {
            Id = p.Id,
            Sku = p.StoreSku ?? p.Product.Sku,
            Name = p.Product.Name,
            Unit = p.Product.Packagings.Where(u => u.IsBaseUnit).Select(u => u.Unit.Code).FirstOrDefault() ?? "",
            CategoryId = p.Product.CategoryId,
            Category = p.Product.Category.Name
        });

    private IQueryable<LedgerEntry> Ledger(Guid storeId) =>
        from m in context.StockMovements.AsNoTracking()
        where m.StoreId == storeId && m.Status == StockMovementStatus.Posted
        join i in context.StockMovementItems on m.Id equals i.StockMovementId
        join l in context.InventoryLots on i.InventoryLotId equals l.Id
        select new LedgerEntry
        {
            ProductId = l.StoreProductId,
            LotId = l.Id,
            LotNumber = l.LotNumber,
            ItemId = i.Id,
            MovementId = m.Id,
            Number = m.MovementNumber,
            Type = m.MovementType,
            PostedAt = m.PostedAt!.Value,
            Quantity = i.QuantityDeltaBase,
            Cost = i.QuantityDeltaBase > 0 ? i.TotalCostSnapshot : -i.TotalCostSnapshot,
            UnitCost = i.UnitCostSnapshot,
            ReceiptId = m.GoodsReceiptId,
            OrderId = m.OrderId,
            DeliveryId = m.DeliveryId,
            StocktakeId = m.StocktakeId,
            ReturnId = m.SalesReturnId
        };

    public async Task<StockCardResponse> GetStockCardAsync(StockCardRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var product = await Products(storeId).SingleOrDefaultAsync(p => p.Id == request.StoreProductId, cancellationToken)
            ?? throw new NotFoundException("Store product", request.StoreProductId);
        if (request.InventoryLotId is { } lotId && !await context.InventoryLots.AsNoTracking()
            .AnyAsync(l => l.Id == lotId && l.StoreProductId == product.Id, cancellationToken))
            throw new NotFoundException("Inventory lot", lotId);

        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to).AddDays(1);
        var ledger = Ledger(storeId).Where(l => l.ProductId == product.Id
            && (request.InventoryLotId == null || l.LotId == request.InventoryLotId));
        var opening = await ledger.Where(l => l.PostedAt < start).SumAsync(l => l.Quantity, cancellationToken);
        var entries = await ledger.Where(l => l.PostedAt >= start && l.PostedAt < end)
            .OrderBy(l => l.PostedAt).ThenBy(l => l.MovementId).ThenBy(l => l.ItemId).ToListAsync(cancellationToken);
        var references = await ReferencesAsync(entries, cancellationToken);
        var balance = opening;
        var lines = entries.Select(l =>
        {
            balance += l.Quantity;
            var reference = ReferenceKey(l);
            return new StockCardLine(l.PostedAt, l.MovementId, l.Number, EnumText.Format(l.Type),
                reference is { } key ? new(key.Type, key.Id, references.GetValueOrDefault(key)) : null,
                l.LotNumber, Math.Max(l.Quantity, 0), l.Quantity < 0 ? -l.Quantity : 0, balance, l.UnitCost);
        }).ToList();
        return new(product.Id, product.Sku, product.Name, product.Unit, from, to, opening, lines, balance);
    }

    // Resolve source labels in batches, including archived documents so a historical card keeps its reference.
    // The most specific source wins: return, stocktake, receipt, delivery, then order.
    private static (string Type, Guid Id)? ReferenceKey(LedgerEntry e) =>
        e.ReturnId is { } r ? ("SALES_RETURN", r) : e.StocktakeId is { } s ? ("STOCKTAKE", s) :
        e.ReceiptId is { } g ? ("GOODS_RECEIPT", g) : e.DeliveryId is { } d ? ("DELIVERY", d) :
        e.OrderId is { } o ? ("ORDER", o) : null;

    private async Task<Dictionary<(string Type, Guid Id), string>> ReferencesAsync(List<LedgerEntry> entries, CancellationToken ct)
    {
        var keys = entries.Select(ReferenceKey).OfType<(string Type, Guid Id)>().Distinct().ToList();
        var result = new Dictionary<(string Type, Guid Id), string>();
        foreach (var group in keys.GroupBy(k => k.Type))
        {
            var ids = group.Select(k => k.Id).ToArray();
            var query = group.Key switch
            {
                "SALES_RETURN" => context.SalesReturns.AsNoTracking().IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Number = x.ReturnNumber }),
                "STOCKTAKE" => context.Stocktakes.AsNoTracking().IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Number = x.StocktakeNumber }),
                "GOODS_RECEIPT" => context.GoodsReceipts.AsNoTracking().IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Number = x.ReceiptNumber }),
                "DELIVERY" => context.Deliveries.AsNoTracking().IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Number = x.DeliveryNumber }),
                _ => context.Orders.AsNoTracking().IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, Number = x.OrderNumber })
            };
            foreach (var row in await query.ToListAsync(ct)) result.Add((group.Key, row.Id), row.Number);
        }
        return result;
    }

    public async Task<InventoryMovementReportResponse> GetMovementAsync(InventoryMovementReportRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to).AddDays(1);
        var products = Products(storeId, request.CategoryId);
        var totals = await (from l in Ledger(storeId)
                            join p in products on l.ProductId equals p.Id
                            where l.PostedAt < end
                            group l by new { l.ProductId, Opening = l.PostedAt < start, l.Type } into g
                            select new { g.Key.ProductId, g.Key.Opening, g.Key.Type, Quantity = g.Sum(l => l.Quantity), Value = g.Sum(l => l.Cost) })
            .ToListAsync(cancellationToken);
        var byProduct = totals.ToLookup(t => t.ProductId);
        var rows = (await products.OrderBy(p => p.Sku).ThenBy(p => p.Id).ToListAsync(cancellationToken)).Select(p =>
        {
            var values = byProduct[p.Id].ToList();
            var opening = values.Where(v => v.Opening).ToList();
            InventoryMovementAmount Bucket(StockMovementType type)
            {
                var bucket = values.Where(v => !v.Opening && v.Type == type).ToList();
                return new(bucket.Sum(v => v.Quantity), CostRounding.RoundMoney(bucket.Sum(v => v.Value)));
            }
            return new InventoryMovementReportRow(p.Id, p.Sku, p.Name, p.Unit,
                opening.Sum(v => v.Quantity), CostRounding.RoundMoney(opening.Sum(v => v.Value)),
                Bucket(StockMovementType.StockIn), Bucket(StockMovementType.ReturnIn), Bucket(StockMovementType.AdjustmentIn),
                Bucket(StockMovementType.Sale), Bucket(StockMovementType.AdjustmentOut), Bucket(StockMovementType.Reversal),
                values.Sum(v => v.Quantity), CostRounding.RoundMoney(values.Sum(v => v.Value)));
        }).ToList();
        return new(from, to, rows, new(rows.Sum(r => r.OpeningValue), rows.Sum(r => r.StockIn.Value),
            rows.Sum(r => r.ReturnIn.Value), rows.Sum(r => r.AdjustmentIn.Value), rows.Sum(r => r.Sale.Value),
            rows.Sum(r => r.AdjustmentOut.Value), rows.Sum(r => r.Reversal.Value), rows.Sum(r => r.ClosingValue)));
    }

    public async Task<InventoryValuationReportResponse> GetValuationAsync(InventoryValuationReportRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var totals = context.InventoryLots.AsNoTracking().GroupBy(l => l.StoreProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(l => l.Balance.QuantityOnHand),
                Value = g.Sum(l => l.Balance.TotalCostValue),
                Expired = g.Sum(l => l.ExpiryDate < today ? l.Balance.TotalCostValue : 0m)
            });
        var data = await (from p in Products(storeId, request.CategoryId)
                          join t in totals on p.Id equals t.ProductId into joined
                          from t in joined.DefaultIfEmpty()
                          orderby p.Sku, p.Id
                          select new
                          {
                              Product = p,
                              Quantity = (long?)t.Quantity ?? 0,
                              Value = (decimal?)t.Value ?? 0,
                              Expired = (decimal?)t.Expired ?? 0
                          }).ToListAsync(cancellationToken);
        var rows = data.Select(d => new InventoryValuationReportRow(d.Product.Id, d.Product.Sku, d.Product.Name,
            d.Product.Category, d.Quantity, d.Quantity == 0 ? null : CostRounding.RoundUnitCost(d.Value / d.Quantity),
            CostRounding.RoundMoney(d.Value), CostRounding.RoundMoney(d.Expired))).ToList();
        var categories = data.Select((d, i) => new { d.Product.CategoryId, d.Product.Category, Value = rows[i].StockValue })
            .GroupBy(d => new { d.CategoryId, d.Category }).OrderBy(g => g.Key.Category)
            .Select(g => new InventoryValuationCategory(g.Key.CategoryId, g.Key.Category, g.Sum(d => d.Value))).ToList();
        return new(rows, categories, new(rows.Sum(r => r.StockValue), rows.Sum(r => r.ExpiredValue)));
    }
}
