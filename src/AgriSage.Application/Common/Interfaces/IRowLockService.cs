namespace AgriSage.Application.Common.Interfaces;

// Pessimistic row locks (SELECT ... FOR UPDATE) for use cases that must not run twice at once, e.g. confirming the
// same Goods Receipt. Must be called inside the use case transaction; the lock ends when it commits or rolls back.
public interface IRowLockService
{
    Task LockGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken);
}
