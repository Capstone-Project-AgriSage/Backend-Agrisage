using AgriSage.Application.Common;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Inventory;

namespace AgriSage.Application.Features.Reports;

public sealed class InventoryPlanningReportService(InventoryPlanningData data) : IInventoryPlanningReportService
{
    public async Task<InventoryVelocityPage> VelocityAsync(InventoryPlanningReportRequest request, CancellationToken token)
    {
        var s = await data.LoadAsync(request.HistoryDays, request.Search, request.CategoryId, null, token, true);
        var lots = s.Lots.ToLookup(l => l.ProductId);
        var sales = s.Sales.ToLookup(l => l.ProductId);
        var observations = s.Observations.ToDictionary(o => o.ProductId, o => BusinessCalendar.Today(o.FirstAt));
        var rows = s.Products.Select(p => {
            var sold = Math.Max(0, sales[p.Id].Sum(x => x.NetQuantity));
            var sellable = lots[p.Id].Where(l => l.Status == "ACTIVE" && (l.Expiry is null || l.Expiry >= s.Today))
                .Sum(l => l.OnHand - l.Reserved);
            var observed = observations.TryGetValue(p.Id, out var first) ? Math.Clamp(s.Today.DayNumber - first.DayNumber, 0, request.HistoryDays) : 0;
            var rate = ReplenishmentMath.DailyDemand(sold, observed, sales[p.Id].Count(x => x.SaleQuantity > 0));
            var cover = ReplenishmentMath.DaysRemaining(sellable, rate);
            var last = sales[p.Id].Where(x => x.SaleQuantity > 0).Select(x => (DateOnly?)x.Day).Max();
            var kind = sold == 0 ? "NO_SALES" : last < s.Today.AddDays(-30) || cover > 90 ? "SLOW" : cover <= 7 ? "FAST" : "STABLE";
            return new InventoryVelocityItem(p.Id, p.Sku, p.Name, p.Unit, sold, lots[p.Id].Sum(l => l.OnHand),
                sellable, rate is { } r ? Math.Round(r, 4) : null, cover, last, kind,
                rate is null ? "INSUFFICIENT_HISTORY" : "SUFFICIENT");
        }).Where(r => string.IsNullOrWhiteSpace(request.Velocity) || r.Velocity == request.Velocity.Trim().ToUpperInvariant())
            .OrderBy(r => r.ProductName).ThenBy(r => r.StoreProductId).ToArray();
        return new(rows.Skip(request.Skip).Take(request.PageSize).ToArray(), request.Page, request.PageSize, rows.Length,
            new(rows.Length, rows.LongCount(r => r.Velocity == "FAST"), rows.LongCount(r => r.Velocity == "SLOW"),
                rows.LongCount(r => r.Velocity == "NO_SALES")), s.Today.AddDays(-request.HistoryDays), s.Today.AddDays(-1));
    }
    public async Task<ExpiryRiskPage> ExpiryRiskAsync(InventoryPlanningReportRequest request, CancellationToken token)
    {
        var s = await data.LoadAsync(request.HistoryDays, request.Search, request.CategoryId, null, token, true);
        var rows = new List<ExpiryRiskItem>();
        var lots = s.Lots.ToLookup(l => l.ProductId);
        var sales = s.Sales.ToLookup(x => x.ProductId);
        var observations = s.Observations.ToDictionary(o => o.ProductId, o => BusinessCalendar.Today(o.FirstAt));
        foreach (var p in s.Products) {
            var all = lots[p.Id].Where(l => l.OnHand > 0).OrderBy(l => l.Expiry ?? DateOnly.MaxValue).ThenBy(l => l.Id).ToArray();
            var days = observations.TryGetValue(p.Id, out var first) ? Math.Clamp(s.Today.DayNumber - first.DayNumber, 0, request.HistoryDays) : 0;
            var rate = ReplenishmentMath.DailyDemand(Math.Max(0, sales[p.Id].Sum(x => x.NetQuantity)), days,
                sales[p.Id].Count(x => x.SaleQuantity > 0));
            var remaining = all.Where(l => l.Status == "ACTIVE" && (l.Expiry is null || l.Expiry >= s.Today))
                .ToDictionary(l => l.Id, l => (decimal)(l.OnHand - l.Reserved));
            var risk = new Dictionary<Guid, long>();
            if (rate.HasValue) {
                for (var offset = 0; offset <= request.WithinDays + 1; offset++) {
                    var day = s.Today.AddDays(offset);
                    foreach (var l in all.Where(l => l.Expiry < day && remaining.ContainsKey(l.Id))) {
                        risk[l.Id] = ReplenishmentMath.CeilingQuantity(remaining[l.Id]);
                        remaining.Remove(l.Id);
                    }
                    var needed = rate.Value;
                    foreach (var l in all.Where(l => remaining.ContainsKey(l.Id))) {
                        var take = Math.Min(needed, remaining[l.Id]);
                        remaining[l.Id] -= take; needed -= take;
                        if (needed <= 0) break;
                    }
                }
            }
            foreach (var l in all.Where(l => l.Expiry.HasValue && l.Expiry <= s.Today.AddDays(request.WithinDays))) {
                long? atRisk = l.Expiry < s.Today ? l.OnHand : risk.TryGetValue(l.Id, out var amount) ? amount : null;
                var value = atRisk.HasValue ? Math.Round(l.Value * atRisk.Value / l.OnHand, 2) : (decimal?)null;
                rows.Add(new(l.Id, p.Id, p.Sku, p.Name, p.Unit, l.Number, l.Expiry!.Value, l.Status,
                    l.OnHand, l.Reserved, atRisk, value, l.Expiry < s.Today ? "EXPIRED"
                        : l.Status != "ACTIVE" ? "HELD_STOCK" : rate is null ? "INSUFFICIENT_HISTORY" : "ESTIMATED"));
            }
        }
        rows = rows.OrderBy(r => r.ExpiryDate).ThenBy(r => r.Sku).ThenBy(r => r.InventoryLotId).ToList();
        return new(rows.Skip(request.Skip).Take(request.PageSize).ToArray(), request.Page, request.PageSize, rows.Count,
            new(rows.Count, rows.LongCount(r => r.AtRiskBaseQuantity > 0), rows.LongCount(r => r.AtRiskBaseQuantity is null),
                rows.Sum(r => r.AtRiskValue ?? 0)), request.WithinDays);
    }
}
