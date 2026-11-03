using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

public sealed class RowLockService(AgriSageDbContext context) : IRowLockService
{
    public async Task LockSalesReturnAsync(Guid salesReturnId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM sales_returns WHERE id = {salesReturnId} FOR UPDATE", cancellationToken);

    public async Task LockStocktakeAsync(Guid stocktakeId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM stocktakes WHERE id = {stocktakeId} FOR UPDATE", cancellationToken);

    public async Task ShareLockLotBalancesAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken)
    {
        if (inventoryLotIds.Count == 0)
        {
            return;
        }

        var ids = inventoryLotIds.Distinct().ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM inventory_lot_balances WHERE inventory_lot_id = ANY({ids}) ORDER BY inventory_lot_id FOR SHARE",
            cancellationToken);
    }

    public async Task LockInventoryLotsAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken)
    {
        if (inventoryLotIds.Count == 0)
        {
            return;
        }

        var ids = inventoryLotIds.Distinct().ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM inventory_lots WHERE id = ANY({ids}) ORDER BY id FOR UPDATE", cancellationToken);
    }

    // FOR NO KEY UPDATE, not FOR UPDATE: it still excludes every other store lock, but does not block a transaction that only
    // inserts a row with a foreign key to the store (that takes FOR KEY SHARE on the store row). With FOR UPDATE a rollover
    // (store lock, then the Farmer lock) deadlocked with an adjust of the same customer (Farmer lock, then its audit entry).
    // The store row's key is never changed or deleted, so nothing relies on blocking those inserts.
    public async Task LockStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM stores WHERE id = {storeId} FOR NO KEY UPDATE", cancellationToken);

    public async Task LockFarmerProfileAsync(Guid farmerProfileId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM farmer_profiles WHERE id = {farmerProfileId} FOR UPDATE", cancellationToken);

    // SELECT ... FOR UPDATE on the receipt row: a second confirmation of the same receipt waits here until the first
    // transaction ends, then sees it already CONFIRMED.
    public async Task LockGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM goods_receipts WHERE id = {goodsReceiptId} FOR UPDATE", cancellationToken);

    public async Task LockOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM orders WHERE id = {orderId} FOR UPDATE", cancellationToken);

    public async Task LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM payments WHERE id = {paymentId} FOR UPDATE", cancellationToken);

    public async Task LockLotBalancesAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken)
    {
        if (inventoryLotIds.Count == 0)
        {
            return;
        }

        var ids = inventoryLotIds.Distinct().ToArray();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM inventory_lot_balances WHERE inventory_lot_id = ANY({ids}) ORDER BY inventory_lot_id FOR UPDATE",
            cancellationToken);
    }

    public async Task LockDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM deliveries WHERE id = {deliveryId} FOR UPDATE", cancellationToken);

    public async Task LockDiagnosisCaseAsync(Guid diagnosisCaseId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM diagnosis_cases WHERE id = {diagnosisCaseId} FOR UPDATE", cancellationToken);
}
