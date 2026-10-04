using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// Sales report (task F1.8, FLOW_1 §10; design §35.22). Revenue is recognized when goods leave, so the
// source is the POSTED SALE stock movements of the period (Vietnam days, on the posting time), not the orders:
//   fulfilledValue = per (movement, order, product) round2(base quantity × the order's price per base unit of that product);
//   a stock movement item names no order line, so the price per base unit is Σ line totals ÷ Σ base quantities of the
//   order's lines of that product (the one line's own price, or the weighted average when the order sells the product in
//   several packagings). A fully handed-over order adds up to its total.
//   costOfGoods   = Σ total_cost_snapshot of those items (the weighted average cost taken when the stock left), rounded per line;
//   grossProfit   = fulfilledValue − costOfGoods;
//   returnValue   = Σ return_value of the lines of returns COMPLETED in the period (read only, L4's data);
//   netSales      = fulfilledValue − returnValue.
// The lines are rounded first and then added, so every grouping gives the same totals and the rows add up to them.
public sealed class SalesReportService(IAgriSageDbContext context) : ISalesReportService
{
    private const string WalkIn = "WALK_IN";
    private const string Ungrouped = "UNGROUPED";

    private sealed record SaleLine(Guid MovementId, Guid OrderId, DateTimeOffset PostedAt, Guid StoreProductId, long BaseQuantity, decimal Cost);

    private sealed record ReturnLine(Guid OrderId, DateTimeOffset CompletedAt, Guid StoreProductId, decimal Value);

    private sealed record OrderInfo(Guid Id, Guid CreatedBy, CustomerType CustomerType, Guid? CustomerGroupId);

    private sealed record Priced(Guid OrderId, DateOnly Day, Guid StoreProductId, decimal Value, decimal Cost);

    private sealed class Accumulator
    {
        public HashSet<Guid> Orders { get; } = [];

        public decimal Fulfilled { get; set; }

        public decimal Cost { get; set; }

        public decimal Returns { get; set; }
    }

    public async Task<SalesReportResponse> GetAsync(SalesReportRequest request, CancellationToken cancellationToken)
    {
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var groupBy = string.IsNullOrWhiteSpace(request.GroupBy) ? "DAY" : request.GroupBy.Trim().ToUpperInvariant();
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to.AddDays(1));

        var sales = await (
            from m in context.StockMovements.AsNoTracking()
            where m.StoreId == storeId && m.MovementType == StockMovementType.Sale && m.Status == StockMovementStatus.Posted
                && m.OrderId != null && m.PostedAt >= start && m.PostedAt < end
            join i in context.StockMovementItems on m.Id equals i.StockMovementId
            join lot in context.InventoryLots on i.InventoryLotId equals lot.Id
            select new SaleLine(m.Id, m.OrderId!.Value, m.PostedAt!.Value, lot.StoreProductId, -i.QuantityDeltaBase, i.TotalCostSnapshot))
            .ToListAsync(cancellationToken);

        var returns = await (
            from r in context.SalesReturns.AsNoTracking()
            where r.StoreId == storeId && r.Status == SalesReturnStatus.Completed && r.CompletedAt >= start && r.CompletedAt < end
            join i in context.SalesReturnItems on r.Id equals i.SalesReturnId
            join lot in context.InventoryLots on i.InventoryLotId equals lot.Id
            select new ReturnLine(r.OrderId, r.CompletedAt!.Value, lot.StoreProductId, i.ReturnValue))
            .ToListAsync(cancellationToken);

        var orderIds = sales.Select(s => s.OrderId).Concat(returns.Select(r => r.OrderId)).Distinct().ToList();
        var orders = (await context.Orders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .Select(o => new OrderInfo(o.Id, o.CreatedBy, o.CustomerType, o.CustomerGroupIdSnapshot))
                .ToListAsync(cancellationToken))
            .ToDictionary(o => o.Id);
        var prices = (await context.OrderItems.AsNoTracking()
                .Where(i => orderIds.Contains(i.OrderId))
                .GroupBy(i => new { i.OrderId, i.StoreProductId })
                .Select(g => new { g.Key.OrderId, g.Key.StoreProductId, Total = g.Sum(i => i.LineTotalAmount), Base = g.Sum(i => i.BaseQuantity) })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => (p.OrderId, p.StoreProductId), p => (p.Total, p.Base));

        // One line per stock movement, order and product; multiply before dividing, then round (coding rule #61).
        var lines = sales
            .GroupBy(s => (s.MovementId, s.OrderId, s.StoreProductId))
            .Select(g =>
            {
                var quantity = g.Sum(s => s.BaseQuantity);
                var value = prices.TryGetValue((g.Key.OrderId, g.Key.StoreProductId), out var price) && price.Base > 0
                    ? CostRounding.RoundMoney(quantity * price.Total / price.Base)
                    : 0m;

                return new Priced(
                    g.Key.OrderId, BusinessCalendar.Today(g.First().PostedAt), g.Key.StoreProductId, value,
                    CostRounding.RoundMoney(g.Sum(s => s.Cost)));
            })
            .ToList();

        var labels = await LabelsAsync(groupBy, lines, returns, orders, cancellationToken);
        var rows = new Dictionary<string, Accumulator>();
        Accumulator Row(string key) => rows.TryGetValue(key, out var row) ? row : rows[key] = new Accumulator();

        foreach (var line in lines)
        {
            var row = Row(KeyOf(groupBy, line.Day, line.OrderId, line.StoreProductId, orders));
            row.Orders.Add(line.OrderId);
            row.Fulfilled += line.Value;
            row.Cost += line.Cost;
        }

        foreach (var line in returns)
        {
            Row(KeyOf(groupBy, BusinessCalendar.Today(line.CompletedAt), line.OrderId, line.StoreProductId, orders)).Returns += line.Value;
        }

        var result = rows
            .Select(r => new SalesReportRow(
                r.Key,
                labels.GetValueOrDefault(r.Key, r.Key),
                r.Value.Orders.Count,
                r.Value.Fulfilled,
                r.Value.Cost,
                r.Value.Fulfilled - r.Value.Cost,
                r.Value.Returns,
                r.Value.Fulfilled - r.Value.Returns))
            .ToList();

        result = groupBy == "DAY"
            ? [.. result.OrderBy(r => r.Key, StringComparer.Ordinal)]
            : [.. result.OrderByDescending(r => r.FulfilledValue).ThenBy(r => r.Label, StringComparer.CurrentCulture).ThenBy(r => r.Key, StringComparer.Ordinal)];

        var fulfilled = result.Sum(r => r.FulfilledValue);
        var cost = result.Sum(r => r.CostOfGoods);
        var returned = result.Sum(r => r.ReturnValue);

        return new SalesReportResponse(
            from, to, groupBy, result,
            new SalesReportTotals(lines.Select(l => l.OrderId).Distinct().Count(), fulfilled, cost, fulfilled - cost, returned, fulfilled - returned));
    }

    private static string KeyOf(string groupBy, DateOnly day, Guid orderId, Guid storeProductId, Dictionary<Guid, OrderInfo> orders) =>
        groupBy switch
        {
            "PRODUCT" => storeProductId.ToString(),
            "STAFF" => orders[orderId].CreatedBy.ToString(),
            "CUSTOMER_GROUP" => orders[orderId].CustomerType == CustomerType.WalkIn
                ? WalkIn
                : orders[orderId].CustomerGroupId?.ToString() ?? Ungrouped,
            _ => day.ToString("yyyy-MM-dd")
        };

    // Names for the keys of the grouping (a day is its own label).
    private async Task<Dictionary<string, string>> LabelsAsync(
        string groupBy,
        List<Priced> lines,
        List<ReturnLine> returns,
        Dictionary<Guid, OrderInfo> orders,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<string, string>();

        switch (groupBy)
        {
            case "PRODUCT":
                var productIds = lines.Select(l => l.StoreProductId).Concat(returns.Select(r => r.StoreProductId)).Distinct().ToList();
                foreach (var product in await context.StoreProducts.AsNoTracking().Where(sp => productIds.Contains(sp.Id))
                             .Select(sp => new { sp.Id, sp.Product.Name, sp.Product.Sku }).ToListAsync(cancellationToken))
                {
                    labels[product.Id.ToString()] = $"{product.Name} ({product.Sku})";
                }

                break;
            case "STAFF":
                var userIds = orders.Values.Select(o => o.CreatedBy).Distinct().ToList();
                foreach (var user in await context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                             .Select(u => new { u.Id, u.FullName }).ToListAsync(cancellationToken))
                {
                    labels[user.Id.ToString()] = user.FullName;
                }

                break;
            case "CUSTOMER_GROUP":
                labels[WalkIn] = "Khách lẻ";
                labels[Ungrouped] = "Chưa phân nhóm";
                var groupIds = orders.Values.Where(o => o.CustomerGroupId is not null).Select(o => o.CustomerGroupId!.Value).Distinct().ToList();
                foreach (var group in await context.CustomerGroups.AsNoTracking().IgnoreQueryFilters().Where(g => groupIds.Contains(g.Id))
                             .Select(g => new { g.Id, g.Name }).ToListAsync(cancellationToken))
                {
                    labels[group.Id.ToString()] = group.Name;
                }

                break;
        }

        return labels;
    }
}
