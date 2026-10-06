using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// OrderBuilder checks credit eligibility for CREDIT orders (flow 3): the farmer needs an active credit profile and an
// active debt account. Tests of other flows that build a CREDIT order call this once for their farmer. Each helper saves
// inside the test's own transaction, which the test session rolls back.
internal static class CreditFixtures
{
    public static async Task EnableCreditAsync(
        AgriSageDbContext context, Guid storeId, Guid farmerProfileId, Guid approvedBy, CancellationToken cancellationToken)
    {
        var tier = new CreditTier(storeId, $"T{Guid.NewGuid():N}"[..12], "Test tier", 20_000_000, 30);
        context.CreditTiers.Add(tier);
        context.FarmerCreditProfiles.Add(new FarmerCreditProfile(storeId, farmerProfileId, 20_000_000, approvedBy, DateTimeOffset.UtcNow, tier.Id));
        context.DebtAccounts.Add(new DebtAccount(storeId, farmerProfileId));
        await context.SaveChangesAsync(cancellationToken);
    }

    // Creating a CREDIT order also checks that the stock is there (flow 3), so these tests receive a lot first.
    public static async Task ReceiveStockAsync(AgriSageDbContext context, Guid storeProductId, long baseQuantity, CancellationToken cancellationToken)
    {
        var lot = new InventoryLot(storeProductId, $"L{Guid.NewGuid():N}"[..10], null, null);
        lot.ReceiveStock(baseQuantity, 5_000m);
        context.InventoryLots.Add(lot);
        await context.SaveChangesAsync(cancellationToken);
    }
}
