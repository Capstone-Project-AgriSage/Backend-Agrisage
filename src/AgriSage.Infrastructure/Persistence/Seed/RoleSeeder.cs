using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

public static class RoleSeeder
{
    public sealed record Entry(RoleCode Code, string Name);

    public static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly<Entry>(
    [
        new(RoleCode.Farmer, "Farmer"),
        new(RoleCode.StoreOwner, "Store Owner"),
        new(RoleCode.SalesStaff, "Sales Staff"),
        new(RoleCode.DeliveryStaff, "Delivery Staff"),
        new(RoleCode.Admin, "Admin")
    ]);

    internal static async Task<int> StageAsync(AgriSageDbContext context, CancellationToken cancellationToken)
    {
        var codes = Entries.Select(entry => entry.Code).ToArray();
        var existing = await context.Roles.IgnoreQueryFilters().AsNoTracking()
            .Where(role => codes.Contains(role.Code)).ToListAsync(cancellationToken);
        var added = 0;
        foreach (var entry in Entries)
        {
            var matches = existing.Where(role => role.Code == entry.Code).ToList();
            foreach (var match in matches)
            {
                SeedLookup.RejectDeleted(match, "roles", entry.Code.ToString());
            }

            if (matches.Count == 0)
            {
                context.Roles.Add(new Role(entry.Code, entry.Name));
                added++;
            }
        }

        return added;
    }
}
