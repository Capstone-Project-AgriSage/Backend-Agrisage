using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Persistence;

public sealed class RowLockService(AgriSageDbContext context) : IRowLockService
{
    // SELECT ... FOR UPDATE on the receipt row: a second confirmation of the same receipt waits here until the first
    // transaction ends, then sees it already CONFIRMED.
    public async Task LockGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM goods_receipts WHERE id = {goodsReceiptId} FOR UPDATE", cancellationToken);
}
