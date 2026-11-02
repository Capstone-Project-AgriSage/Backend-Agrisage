using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

public sealed class ReplenishmentService(InventoryPlanningData data, IAgriSageDbContext context) : IReplenishmentService
{
    public async Task<ReplenishmentPage> GetAsync(string view, ReplenishmentRequest request, CancellationToken token)
    {
        var snapshot = await data.LoadAsync(request.HistoryDays, request.Search, request.CategoryId, request.StoreProductId, token);
        var today = snapshot.Today;
        var through = today.AddDays(Math.Max(request.HorizonDays, 90));
        var rules = await (from r in context.SeasonalRules.AsNoTracking()
                           where r.StoreId == snapshot.StoreId && r.IsActive && r.EndsOn >= today && r.StartsOn <= through
                           join i in context.SeasonalRuleItems on r.Id equals i.SeasonalRuleId
                           select new { i.StoreProductId, Rule = new SeasonalInfluence(r.Id, r.Name, r.CropType,
                               r.Region, r.StartsOn, r.EndsOn, r.DemandFactor, i.TargetStockLevelBase), r.PrepareDays })
                           .ToListAsync(token);
        var lots = snapshot.Lots.ToLookup(l => l.ProductId);
        var sales = snapshot.Sales.ToLookup(s => s.ProductId);
        var observed = snapshot.Observations.ToDictionary(o => o.ProductId, o => BusinessCalendar.Today(o.FirstAt));
        var seasonal = rules.ToLookup(r => r.StoreProductId);
        var rows = new List<ReplenishmentItem>();
        foreach (var p in snapshot.Products.Where(p => p.Enabled))
        {
            var productLots = lots[p.Id].ToArray();
            var onHand = productLots.Sum(l => l.OnHand);
            var reserved = productLots.Sum(l => l.Reserved);
            var sellable = productLots.Where(l => l.Status == "ACTIVE" && (l.Expiry is null || l.Expiry >= today))
                .Sum(l => l.OnHand - l.Reserved);
            var sold = Math.Max(0, sales[p.Id].Sum(s => s.NetQuantity));
            var days = observed.TryGetValue(p.Id, out var first) ? Math.Clamp(today.DayNumber - first.DayNumber, 0, request.HistoryDays) : 0;
            var saleDays = sales[p.Id].Count(s => s.SaleQuantity > 0);
            var rate = ReplenishmentMath.DailyDemand(sold, days, saleDays);
            var status = rate is null ? "INSUFFICIENT_HISTORY" : rate == 0 ? "NO_DEMAND" : "SUFFICIENT";
            var applicable = seasonal[p.Id].Where(r => today >= r.Rule.StartsOn.AddDays(-r.PrepareDays)).ToArray();
            var factor = applicable.Select(r => r.Rule.DemandFactor).DefaultIfEmpty(1m).Max();
            long? seasonalTarget = applicable.Where(r => r.Rule.TargetStockLevelBase.HasValue)
                .Select(r => r.Rule.TargetStockLevelBase).DefaultIfEmpty(null).Max();
            var target = ReplenishmentMath.Target(p.Target, p.Minimum, rate, p.Lead, p.Coverage, p.Safety, factor, seasonalTarget);
            var quantity = ReplenishmentMath.PurchaseQuantity(target, sellable, p.Multiple);
            var currentRate = rate * seasonal[p.Id].Where(r => r.Rule.StartsOn <= today && r.Rule.EndsOn >= today)
                .Select(r => r.Rule.DemandFactor).DefaultIfEmpty(1m).Max();
            var daysOfStock = sellable == 0 ? 0m : ReplenishmentMath.DaysRemaining(sellable, currentRate);
            // Dates are estimates under the current sales rate, not promises of a supplier arrival.
            DateOnly? stockout = sellable == 0 ? today : daysOfStock is { } d && d <= 3650
                ? today.AddDays((int)decimal.Floor(d)) : null;
            DateOnly? orderDate = stockout?.AddDays(-p.Lead);
            var leadRisk = currentRate is > 0 && sellable <= currentRate * p.Lead + p.Safety;
            var reasons = new List<string>();
            if (sellable <= 0) reasons.Add("OUT_OF_STOCK");
            if (p.Minimum.HasValue && sellable < p.Minimum) reasons.Add("LOW_STOCK");
            if (leadRisk) reasons.Add("LEAD_TIME_RISK");
            if (applicable.Length > 0 && target > sellable) reasons.Add("SEASONAL");
            if (target is null || p.PackagingId is null) reasons.Add("NEEDS_CONFIGURATION");
            if (view == "suggestions" && !reasons.Any(r => r != "NEEDS_CONFIGURATION")) continue;
            if (view == "seasonal" && applicable.Length == 0) continue;
            var priority = sellable == 0 || (daysOfStock.HasValue && daysOfStock <= p.Lead)
                ? "HIGH" : reasons.Any(r => r is "LOW_STOCK" or "LEAD_TIME_RISK" or "SEASONAL") ? "MEDIUM" : "LOW";
            var warnings = new List<string> { "Dự báo từ lượng bán thực tế; chưa khôi phục nhu cầu bị mất trong những ngày hết hàng." };
            if (rate is null) warnings.Add("Cần ít nhất 14 ngày theo dõi và 3 ngày có xuất bán để dự báo.");
            if (p.PackagingId is null) warnings.Add("Chưa cấu hình quy cách mua đang hoạt động; không thể quy đổi lượng đặt nhập.");
            if (productLots.Any(l => l.Expiry != null && l.OnHand > 0))
                warnings.Add("Ngày hết hàng ước tính theo tốc độ bán hiện tại, chưa trừ tổn thất hết hạn hoặc thay đổi nhu cầu sắp tới. Xem báo cáo rủi ro hạn dùng.");
            var forecast = rate.HasValue ? Enumerable.Range(0, request.HorizonDays).Select(offset => {
                var day = today.AddDays(offset);
                var f = seasonal[p.Id].Where(r => r.Rule.StartsOn <= day && r.Rule.EndsOn >= day)
                    .Select(r => r.Rule.DemandFactor).DefaultIfEmpty(1m).Max();
                return new ForecastPoint(day, Math.Round(rate.Value * f, 3));
            }).ToArray() : [];
            var explanation = target is null ? "Chưa đủ dữ liệu hoặc mức tồn mục tiêu để đề xuất số lượng nhập."
                : $"Còn {sellable} {p.Unit} có thể bán; mục tiêu {target} {p.Unit}; chờ nhập {p.Lead} ngày."
                    + (applicable.Length > 0 ? " Đã xét quy tắc mùa vụ theo mức cao nhất, không cộng trùng." : "");
            rows.Add(new(p.Id, p.Sku, p.Name, p.Unit, onHand, reserved, sellable, p.Minimum, target,
                quantity, p.PackagingId is null || quantity is null ? null : quantity / Math.Max(1, p.Multiple),
                p.PackagingId, p.PurchaseUnit, p.Multiple, rate is { } value ? Math.Round(value, 4) : null,
                rate.HasValue ? forecast.Sum(f => f.ExpectedBaseQuantity) : null, daysOfStock, stockout,
                orderDate, p.Lead, sold, days, saleDays, priority, status, reasons, explanation, warnings,
                applicable.Select(r => r.Rule).ToArray(), forecast));
        }
        IEnumerable<ReplenishmentItem> filtered = rows;
        if (!string.IsNullOrWhiteSpace(request.Priority)) filtered = filtered.Where(r => r.Priority == request.Priority.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(request.Reason)) filtered = filtered.Where(r => r.ReasonCodes.Contains(request.Reason.Trim().ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(request.DataStatus)) filtered = filtered.Where(r => r.DataStatus == request.DataStatus.Trim().ToUpperInvariant());
        rows = filtered.ToList();
        var summary = new ReplenishmentSummary(rows.Count, rows.LongCount(r => r.SellableBaseQuantity == 0),
            rows.LongCount(r => r.Priority == "HIGH"), rows.LongCount(r => r.ReasonCodes.Contains("NEEDS_CONFIGURATION")),
            rows.LongCount(r => r.DataStatus == "INSUFFICIENT_HISTORY"), rows.LongCount(r => r.Seasons.Count > 0));
        Func<ReplenishmentItem, IComparable> key = request.SortBy.Trim().ToLowerInvariant() switch {
            "name" => r => r.ProductName,
            "stockout" => r => r.DaysOfStock ?? decimal.MaxValue,
            "quantity" => r => r.SuggestedBaseQuantity ?? -1L,
            _ => r => r.Priority == "HIGH" ? 0 : r.Priority == "MEDIUM" ? 1 : 2
        };
        var ordered = request.Descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        return new(ordered.ThenBy(r => r.Sku).ThenBy(r => r.StoreProductId).Skip(request.Skip).Take(request.PageSize).ToArray(),
            request.Page, request.PageSize, rows.Count, summary, snapshot.GeneratedAt,
            today.AddDays(-request.HistoryDays), today.AddDays(-1), request.HorizonDays);
    }
}
