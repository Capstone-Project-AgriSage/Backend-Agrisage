using AgriSage.Domain.Common;

namespace AgriSage.Infrastructure.Persistence.Seed;

internal static class SeedLookup
{
    public static void RejectDeleted(SoftDeletableEntity? entity, string table, string code)
    {
        if (entity?.IsDeleted == true)
        {
            throw new ReferenceSeedException($"Cannot seed {table}: code '{code}' is soft-deleted. Review it manually; seed never restores or overwrites rows.");
        }
    }
}
