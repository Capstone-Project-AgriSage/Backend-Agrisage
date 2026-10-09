using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// SQL projection of one posted movement/product line or completed return line. No entities leave this feature.
internal sealed class RevenueLedger(IAgriSageDbContext db)
{
    internal sealed class Entry
    {
        public DateTimeOffset At { get; init; }
        public Guid OrderId { get; init; }
        public Guid ProductId { get; init; }
        public Guid CategoryId { get; init; }
        public Guid StaffId { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? GroupId { get; init; }
        public OrderSource Source { get; init; }
        public SettlementType Settlement { get; init; }
        public bool IsSale { get; init; }
        public decimal Value { get; init; }
        public decimal Cost { get; init; }
        public decimal Returned { get; init; }
    }

    internal IQueryable<Entry> Query(Guid store, RevenueFilterRequest request)
    {
        var start = BusinessCalendar.StartOfDay(request.FromDate!.Value);
        var end = BusinessCalendar.StartOfDay(request.ToDate!.Value).AddDays(1);
        var orders = db.Orders.AsNoTracking().Where(o => o.StoreId == store);
        var prices = db.OrderItems.AsNoTracking().Where(i => orders.Any(o => o.Id == i.OrderId))
            .GroupBy(i => new { i.OrderId, i.StoreProductId })
            .Select(g => new { g.Key.OrderId, ProductId = g.Key.StoreProductId,
                Value = g.Sum(i => i.LineTotalAmount), Quantity = g.Sum(i => i.BaseQuantity) });
        var movements = from m in db.StockMovements.AsNoTracking()
            join i in db.StockMovementItems.AsNoTracking() on m.Id equals i.StockMovementId
            join lot in db.InventoryLots.AsNoTracking() on i.InventoryLotId equals lot.Id
            where m.StoreId == store && m.MovementType == StockMovementType.Sale && m.Status == StockMovementStatus.Posted
                && m.OrderId != null && m.PostedAt >= start && m.PostedAt < end
            group i by new { m.Id, OrderId = m.OrderId!.Value, At = m.PostedAt!.Value, ProductId = lot.StoreProductId } into g
            select new { g.Key.OrderId, g.Key.At, g.Key.ProductId, Quantity = -g.Sum(i => i.QuantityDeltaBase), Cost = g.Sum(i => i.TotalCostSnapshot) };
        // PostgreSQL numeric round(value, 2) is midpoint-away-from-zero, matching CostRounding.RoundMoney.
        var sales = from m in movements
            join price in prices on new { m.OrderId, m.ProductId } equals new { price.OrderId, price.ProductId }
            join o in orders on m.OrderId equals o.Id
            join p in db.StoreProducts.IgnoreQueryFilters().AsNoTracking() on m.ProductId equals p.Id
            select new Entry { At = m.At, OrderId = o.Id, ProductId = m.ProductId, CategoryId = p.Product.CategoryId,
                StaffId = o.CreatedBy, CustomerId = o.FarmerProfileId, GroupId = o.CustomerGroupIdSnapshot,
                Source = o.Source, Settlement = o.SettlementType, IsSale = true,
                Value = price.Quantity > 0 ? Math.Round(m.Quantity * price.Value / price.Quantity, 2) : 0m,
                Cost = Math.Round(m.Cost, 2), Returned = 0m };
        var returns = from r in db.SalesReturns.AsNoTracking()
            join i in db.SalesReturnItems.AsNoTracking() on r.Id equals i.SalesReturnId
            join lot in db.InventoryLots.AsNoTracking() on i.InventoryLotId equals lot.Id
            join p in db.StoreProducts.IgnoreQueryFilters().AsNoTracking() on lot.StoreProductId equals p.Id
            join o in orders on r.OrderId equals o.Id
            where r.StoreId == store && r.Status == SalesReturnStatus.Completed && r.CompletedAt >= start && r.CompletedAt < end
            select new Entry { At = r.CompletedAt!.Value, OrderId = o.Id, ProductId = lot.StoreProductId,
                CategoryId = p.Product.CategoryId, StaffId = o.CreatedBy, CustomerId = o.FarmerProfileId,
                GroupId = o.CustomerGroupIdSnapshot, Source = o.Source, Settlement = o.SettlementType,
                IsSale = false, Value = 0m, Cost = 0m, Returned = i.ReturnValue };
        var query = sales.Concat(returns);
        if (request.StoreProductId is { } product) query = query.Where(e => e.ProductId == product);
        if (request.CategoryId is { } category) query = query.Where(e => e.CategoryId == category);
        if (request.StaffUserId is { } staff) query = query.Where(e => e.StaffId == staff);
        if (request.FarmerProfileId is { } customer) query = query.Where(e => e.CustomerId == customer);
        if (request.CustomerGroupId is { } group) query = query.Where(e => e.GroupId == group);
        if (EnumText.TryParse<OrderSource>(request.Source, out var source)) query = query.Where(e => e.Source == source);
        if (EnumText.TryParse<SettlementType>(request.SettlementType, out var settlement)) query = query.Where(e => e.Settlement == settlement);
        return query;
    }
}
