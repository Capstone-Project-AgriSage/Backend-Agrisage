using System.Globalization;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

public sealed class RevenueReportService(IAgriSageDbContext db, ReportScope scope) : IRevenueReportService
{
    private sealed class Aggregate
    {
        public DateOnly Day { get; init; }
        public int Year { get; init; }
        public int Month { get; init; }
        public Guid? Id { get; init; }
        public int Code { get; init; }
        public OrderSource Source { get; init; }
        public SettlementType Settlement { get; init; }
        public int Orders { get; init; }
        public decimal Value { get; init; }
        public decimal Cost { get; init; }
        public decimal Returned { get; init; }
    }

    internal static RevenueMetrics Metrics(int orders, decimal value, decimal cost, decimal returned) =>
        new(orders, value, cost, value - cost, returned, value - returned,
            orders == 0 ? 0m : CostRounding.RoundMoney(value / orders),
            value == 0 ? null : CostRounding.RoundMoney((value - cost) * 100m / value));

    private static async Task<RevenueMetrics> TotalsAsync(IQueryable<RevenueLedger.Entry> query, CancellationToken token)
    {
        var total = await query.GroupBy(e => 1).Select(g => new
        {
            Orders = g.Where(e => e.IsSale).Select(e => e.OrderId).Distinct().Count(),
            Value = g.Sum(e => e.Value), Cost = g.Sum(e => e.Cost), Returned = g.Sum(e => e.Returned)
        }).SingleOrDefaultAsync(token);
        return total is null ? Metrics(0, 0, 0, 0) : Metrics(total.Orders, total.Value, total.Cost, total.Returned);
    }

    public async Task<RevenueSummaryResponse> SummaryAsync(RevenueSummaryRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var length = to.DayNumber - from.DayNumber + 1;
        var previousFrom = from.AddDays(-length);
        var previousTo = from.AddDays(-1);
        var ledger = new RevenueLedger(db);
        var current = await TotalsAsync(ledger.Query(store, request), token);
        var previous = await TotalsAsync(ledger.Query(store, request with { FromDate = previousFrom, ToDate = previousTo }), token);
        return new(from, to, current, previousFrom, previousTo, previous,
            previous.NetSales == 0 ? null : CostRounding.RoundMoney((current.NetSales - previous.NetSales) * 100m / Math.Abs(previous.NetSales)));
    }

    public async Task<RevenueReportResponse> GetAsync(RevenueReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var by = request.GroupBy?.Trim().ToUpperInvariant() ?? "DAY";
        var query = new RevenueLedger(db).Query(store, request);
        var totals = await TotalsAsync(query, token);
        // Npgsql translates the Vietnam date, Monday week boundary, distinct order count and sums to SQL.
        var grouped = query.GroupBy(e => new
        {
            Day = by == "DAY" ? DateOnly.FromDateTime(e.At.UtcDateTime.AddHours(7))
                : by == "WEEK" ? DateOnly.FromDateTime(e.At.UtcDateTime.AddHours(7))
                    .AddDays(-(((int)e.At.UtcDateTime.AddHours(7).DayOfWeek + 6) % 7)) : DateOnly.MinValue,
            Year = by == "MONTH" ? e.At.UtcDateTime.AddHours(7).Year : 0,
            Month = by == "MONTH" ? e.At.UtcDateTime.AddHours(7).Month : 0,
            Id = by == "PRODUCT" ? (Guid?)e.ProductId : by == "CATEGORY" ? e.CategoryId : by == "STAFF" ? e.StaffId
                : by == "CUSTOMER" ? e.CustomerId : by == "CUSTOMER_GROUP" ? e.GroupId : null,
            Source = by == "SOURCE" ? e.Source : OrderSource.Counter,
            Settlement = by == "SETTLEMENT" ? e.Settlement : SettlementType.FullPayment,
            Code = by == "CUSTOMER_GROUP" && e.CustomerId == null ? 1 : 0
        }).Select(g => new Aggregate
        {
            Day = g.Key.Day, Year = g.Key.Year, Month = g.Key.Month, Id = g.Key.Id, Code = g.Key.Code,
            Source = g.Key.Source, Settlement = g.Key.Settlement,
            Orders = g.Where(e => e.IsSale).Select(e => e.OrderId).Distinct().Count(),
            Value = g.Sum(e => e.Value), Cost = g.Sum(e => e.Cost), Returned = g.Sum(e => e.Returned)
        });
        int count;
        List<Aggregate> aggregates;
        if (by is "DAY" or "WEEK" or "MONTH")
        {
            var existing = (await grouped.ToListAsync(token)).ToDictionary(a => Key(by, a));
            aggregates = [];
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var bucket = new Aggregate { Day = by == "WEEK" ? day.AddDays(-(((int)day.DayOfWeek + 6) % 7)) : day,
                    Year = day.Year, Month = day.Month };
                var key = Key(by, bucket);
                if (aggregates.Count == 0 || Key(by, aggregates[^1]) != key)
                    aggregates.Add(existing.GetValueOrDefault(key) ?? bucket);
            }
            count = aggregates.Count;
            aggregates = aggregates.Skip((int)Math.Min((long)(request.Page - 1) * request.PageSize, count)).Take(request.PageSize).ToList();
        }
        else
        {
            count = await grouped.CountAsync(token);
            var skip = (int)Math.Min((long)(request.Page - 1) * request.PageSize, count);
            aggregates = await grouped.OrderByDescending(a => a.Value).ThenBy(a => a.Id).ThenBy(a => a.Code).ThenBy(a => a.Source).ThenBy(a => a.Settlement)
                .Skip(skip).Take(request.PageSize).ToListAsync(token);
        }
        var labels = await LabelsAsync(by, aggregates.Where(a => a.Id != null).Select(a => a.Id!.Value).ToList(), token);
        var rows = aggregates.Select(a =>
        {
            var key = Key(by, a);
            var metric = Metrics(a.Orders, a.Value, a.Cost, a.Returned);
            return new RevenueReportRow(key, labels.GetValueOrDefault(key, key == "WALK_IN" ? "Khách lẻ" : key == "UNGROUPED" ? "Chưa phân nhóm" : key),
                metric.OrderCount, metric.FulfilledValue, metric.CostOfGoods, metric.GrossProfit, metric.ReturnValue,
                metric.NetSales, metric.AverageOrderValue, metric.GrossMarginPercent);
        }).ToList();
        return new(from, to, by, rows, request.Page, request.PageSize, count, (count + request.PageSize - 1) / request.PageSize, totals);
    }

    private static string Key(string by, Aggregate a) => by switch
    {
        "DAY" or "WEEK" => a.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "MONTH" => $"{a.Year:D4}-{a.Month:D2}",
        "SOURCE" => EnumText.Format(a.Source),
        "SETTLEMENT" => EnumText.Format(a.Settlement),
        "CUSTOMER" => a.Id?.ToString() ?? "WALK_IN",
        "CUSTOMER_GROUP" => a.Code == 1 ? "WALK_IN" : a.Id?.ToString() ?? "UNGROUPED",
        _ => a.Id?.ToString() ?? "UNKNOWN"
    };

    private async Task<Dictionary<string, string>> LabelsAsync(string by, List<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        return by switch
        {
            "PRODUCT" => await db.StoreProducts.IgnoreQueryFilters().AsNoTracking().Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, Label = p.Product.Name + " (" + (p.StoreSku ?? p.Product.Sku) + ")" })
                .ToDictionaryAsync(p => p.Id.ToString(), p => p.Label, token),
            "CATEGORY" => await db.Categories.IgnoreQueryFilters().AsNoTracking().Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id.ToString(), c => c.Name, token),
            "STAFF" => await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id.ToString(), u => u.FullName, token),
            "CUSTOMER" => await db.FarmerProfiles.IgnoreQueryFilters().AsNoTracking().Where(f => ids.Contains(f.Id))
                .Select(f => new { f.Id, f.User.FullName }).ToDictionaryAsync(f => f.Id.ToString(), f => f.FullName, token),
            "CUSTOMER_GROUP" => await db.CustomerGroups.IgnoreQueryFilters().AsNoTracking().Where(g => ids.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id.ToString(), g => g.Name, token),
            _ => []
        };
    }
}
