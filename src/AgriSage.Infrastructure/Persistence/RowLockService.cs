using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

public sealed class RowLockService(AgriSageDbContext context) : IRowLockService
{
    public async Task LockStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM stores WHERE id = {storeId} FOR UPDATE", cancellationToken);

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
}
