using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Order/payment fixtures use the real eligibility service after L3 integration. Configure an eligible customer
// instead of relying on the old placeholder that used to accept every credit draft.
internal static class TestCreditSetup
{
    public static async Task StockAsync(AgriSageDbContext context, Guid storeProductId, DateTimeOffset now, CancellationToken token)
    {
        var lot = new AgriSage.Domain.Features.Inventory.Entities.InventoryLot(storeProductId,
            "T" + Guid.NewGuid().ToString("N")[..12], null, DateOnly.FromDateTime(now.UtcDateTime).AddDays(365));
        lot.ReceiveStock(1000, 1000m);
        context.InventoryLots.Add(lot);
        await context.SaveChangesAsync(token);
    }
    public static async Task GrantAsync(AgriSageDbContext context, Guid storeId, Guid farmerId, Guid actorId,
        DateTimeOffset now, CancellationToken token)
    {
        var tier = new CreditTier(storeId, "T" + Guid.NewGuid().ToString("N")[..12], "Test credit", 10_000_000m, 30);
        context.AddRange(tier, new FarmerCreditProfile(storeId, farmerId, 10_000_000m, actorId, now, tier.Id),
            new DebtAccount(storeId, farmerId));
        await context.SaveChangesAsync(token);
    }
}
