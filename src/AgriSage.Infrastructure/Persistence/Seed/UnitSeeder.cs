using AgriSage.Domain.Features.Products.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

public static class UnitSeeder
{
    public sealed record Entry(string Code, string Name, string Symbol);

    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly<Entry>(
    [
        new("BOTTLE", "Chai", "chai"),
        new("BOX", "Hộp", "hộp"),
        new("CARTON", "Thùng", "thùng"),
        new("BAG", "Bao", "bao"),
        new("PACK", "Gói", "gói"),
        new("KG", "Kilogram", "kg"),
        new("GRAM", "Gram", "g"),
        new("LITER", "Lít", "l"),
        new("ML", "Mililit", "ml")
    ]);

    internal static async Task<int> StageAsync(AgriSageDbContext context, CancellationToken cancellationToken)
    {
        var codes = Entries.Select(entry => entry.Code).ToArray();
        var existing = await context.Units.IgnoreQueryFilters().AsNoTracking()
            .Where(unit => codes.Contains(unit.Code)).ToDictionaryAsync(unit => unit.Code, cancellationToken);
        var added = 0;
        foreach (var entry in Entries)
        {
            existing.TryGetValue(entry.Code, out var unit);
            SeedLookup.RejectDeleted(unit, "units", entry.Code);
            if (unit is null)
            {
                context.Units.Add(new Unit(entry.Code, entry.Name, entry.Symbol));
                added++;
            }
        }

        return added;
    }
}
