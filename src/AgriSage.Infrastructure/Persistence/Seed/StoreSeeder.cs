using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence.Seed;

public static class StoreSeeder
{
    internal static async Task<int> StageAsync(
        AgriSageDbContext context, SeedStoreOptions options, CancellationToken cancellationToken)
    {
        var store = await context.Stores.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(store => store.Code == options.Code, cancellationToken);
        SeedLookup.RejectDeleted(store, "stores", options.Code);
        var otherActiveStore = await context.Stores.AsNoTracking()
            .AnyAsync(store => store.Code != options.Code && store.Status == StoreStatus.Active, cancellationToken);
        if (otherActiveStore)
        {
            throw new ReferenceSeedException("Another ACTIVE store exists. Review the operational store before seeding; seed never changes existing stores.");
        }

        if (store is not null)
        {
            return 0;
        }

        context.Stores.Add(new Store(options.Code, options.Name, options.AddressLine, options.Province,
            options.PhoneNumber, options.Email, options.TaxCode, options.Ward, options.District));
        return 1;
    }
}
