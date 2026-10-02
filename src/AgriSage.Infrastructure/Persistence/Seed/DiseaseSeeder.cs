using AgriSage.Domain.Features.Diagnosis.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

public static class DiseaseSeeder
{
    public sealed record Entry(string Code, string Name, bool IsHealthyClass = false);

    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly<Entry>(
    [
        new("LEAF_BLAST", "Leaf Blast"),
        new("BACTERIAL_LEAF_BLIGHT", "Bacterial Leaf Blight"),
        new("BROWN_SPOT", "Brown Spot"),
        new("SHEATH_BLIGHT", "Sheath Blight"),
        new("HEALTHY", "Healthy", true)
    ]);

    internal static async Task<int> StageAsync(AgriSageDbContext context, CancellationToken cancellationToken)
    {
        var codes = Entries.Select(entry => entry.Code).ToArray();
        var existing = await context.Diseases.IgnoreQueryFilters().AsNoTracking()
            .Where(disease => codes.Contains(disease.Code)).ToDictionaryAsync(disease => disease.Code, cancellationToken);
        var added = 0;
        foreach (var entry in Entries)
        {
            existing.TryGetValue(entry.Code, out var disease);
            SeedLookup.RejectDeleted(disease, "diseases", entry.Code);
            if (disease is null)
            {
                context.Diseases.Add(new Disease(entry.Code, entry.Name, entry.IsHealthyClass));
                added++;
            }
        }

        return added;
    }
}
