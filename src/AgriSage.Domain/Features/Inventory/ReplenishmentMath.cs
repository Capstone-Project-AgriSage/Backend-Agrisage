namespace AgriSage.Domain.Features.Inventory;

// Pure planning rules. No changes to the physical ledger and no invented demand for sparse history.
public static class ReplenishmentMath
{
    public static decimal? DailyDemand(long netSold, int observedDays, int saleDays) =>
        observedDays >= 14 && saleDays >= 3 ? Math.Max(0m, (decimal)netSold / observedDays) : null;

    public static long CeilingQuantity(decimal value) => checked((long)decimal.Ceiling(Math.Max(0m, value)));

    public static long? Target(long? fixedTarget, long? minimum, decimal? demand, int leadDays,
        int coverageDays, long safety, decimal seasonalFactor, long? seasonalTarget)
    {
        decimal? target = fixedTarget.HasValue ? fixedTarget.Value
            : demand is { } rate ? rate * (leadDays + coverageDays) + safety : minimum;
        if (target.HasValue) target *= seasonalFactor;
        if (seasonalTarget.HasValue) target = Math.Max(target ?? 0, seasonalTarget.Value);
        if (target.HasValue && minimum.HasValue) target = Math.Max(target.Value, minimum.Value);
        return target.HasValue ? CeilingQuantity(target.Value) : null;
    }

    public static long? PurchaseQuantity(long? target, long sellable, long multiple)
    {
        if (target is null) return null;
        var needed = Math.Max(0m, (decimal)target.Value - sellable);
        return CeilingQuantity(decimal.Ceiling(needed / Math.Max(1, multiple)) * Math.Max(1, multiple));
    }

    public static decimal? DaysRemaining(long sellable, decimal? demand) =>
        demand is > 0 ? Math.Round(sellable / demand.Value, 1) : null;
}
