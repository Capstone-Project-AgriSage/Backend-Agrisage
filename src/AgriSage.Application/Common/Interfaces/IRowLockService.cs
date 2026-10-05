namespace AgriSage.Application.Common.Interfaces;

// Pessimistic row locks (SELECT ... FOR UPDATE) for use cases that must not run twice at once, e.g. confirming the
// same Goods Receipt. Must be called inside the use case transaction; the lock ends when it commits or rolls back.
public interface IRowLockService
{
    // Customer/group mutations lock the store first, then the Farmer. This also serializes first-profile creation.
    Task LockStoreAsync(Guid storeId, CancellationToken cancellationToken);

    Task LockFarmerProfileAsync(Guid farmerProfileId, CancellationToken cancellationToken);

    Task LockGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken cancellationToken);

    // Serializes payments, confirmation, fulfillment and cancellation of one order (api-flows README §3.1: one method per row type).
    Task LockOrderAsync(Guid orderId, CancellationToken cancellationToken);

    // Serializes cancelling or settling one payment against its payOS webhook.
    Task LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken);

    // Serializes reservation and issue of stock: locks the balances of the given lots in id order (no deadlock between
    // two orders that share lots). Nothing to lock for an empty list.
    Task LockLotBalancesAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken);

    // Serializes changes of one delivery note (assign, lots, dispatch, attempts, cancel). Callers that also lock the
    // order lock the order first.
    Task LockDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken);

    // Serializes lot status transitions; callers re-read the eligible rows after acquiring the locks.
    Task LockInventoryLotsAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken);

    Task LockStocktakeAsync(Guid stocktakeId, CancellationToken cancellationToken);

    // Snapshot readers use FOR SHARE so a balance writer cannot change the quantity before snapshot_at is taken.
    Task ShareLockLotBalancesAsync(IReadOnlyCollection<Guid> inventoryLotIds, CancellationToken cancellationToken);

    Task LockSalesReturnAsync(Guid salesReturnId, CancellationToken cancellationToken);
}
