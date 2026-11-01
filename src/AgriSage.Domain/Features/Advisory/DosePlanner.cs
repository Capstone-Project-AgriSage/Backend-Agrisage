namespace AgriSage.Domain.Features.Advisory;

// How much of a product a field needs and in which packagings (api-flows/ADVISORY.md V6). Pure: no database, no clock.
public static class DosePlanner
{
    private const decimal SquareMetresPerHectare = 10_000m;

    public sealed record SalePack(Guid PackagingId, string Name, long ConversionToBase, decimal UnitPrice);

    public sealed record Line(Guid PackagingId, string Name, long Quantity, decimal UnitPrice, decimal LineAmount, long BaseQuantity);

    public sealed record Plan(long BaseQuantity, decimal TotalAmount, IReadOnlyList<Line> Lines);

    // Base units needed: dose per hectare and per application, times the applications and the area, rounded up so the
    // field is never under-supplied. Null when there is no dose or no area.
    public static long? RequiredBaseQuantity(decimal? dosePerHaBase, int applications, decimal? areaSqm)
    {
        if (dosePerHaBase is not > 0 || areaSqm is not > 0 || applications < 1)
        {
            return null;
        }

        return (long)Math.Ceiling(dosePerHaBase.Value * applications * areaSqm.Value / SquareMetresPerHectare);
    }

    // Splits the need into whole packagings: as many of the largest as fit, the remainder in the next smaller ones, and
    // the last remainder rounded up in the smallest packaging. Null without a need or without a priced packaging.
    public static Plan? Split(long? requiredBaseQuantity, IReadOnlyCollection<SalePack> packs)
    {
        if (requiredBaseQuantity is not > 0 || packs.Count == 0 || packs.Any(p => p.ConversionToBase <= 0 || p.UnitPrice < 0))
        {
            return null;
        }

        var ordered = packs.OrderByDescending(p => p.ConversionToBase).ThenBy(p => p.UnitPrice).ToList();
        var remaining = requiredBaseQuantity.Value;
        var lines = new List<Line>();

        for (var index = 0; index < ordered.Count && remaining > 0; index++)
        {
            var pack = ordered[index];
            var isSmallest = index == ordered.Count - 1;
            var quantity = isSmallest
                ? (remaining + pack.ConversionToBase - 1) / pack.ConversionToBase
                : remaining / pack.ConversionToBase;

            if (quantity == 0)
            {
                continue;
            }

            lines.Add(new Line(pack.PackagingId, pack.Name, quantity, pack.UnitPrice, quantity * pack.UnitPrice, quantity * pack.ConversionToBase));
            remaining = isSmallest ? 0 : remaining - quantity * pack.ConversionToBase;
        }

        return new Plan(lines.Sum(l => l.BaseQuantity), lines.Sum(l => l.LineAmount), lines);
    }
}
