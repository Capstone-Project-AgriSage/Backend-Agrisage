using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.GoodsReceipts;

// Use case "Confirm Goods Receipt" (database design §7.1). The GoodsReceipt use case owns the transaction:
//   lock the receipt → validate → find or create the logical Lots → add stock at cost (weighted average)
//   → STOCK_IN movement POSTED → receipt CONFIRMED → one SaveChanges → commit.
// Nothing is saved midway; any failure rolls everything back.
public sealed class GoodsReceiptConfirmer(
    IAgriSageDbContext context,
    IRowLockService locks,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IDatabaseErrorClassifier databaseErrors)
{
    // Logical lot identity (database design §24): store product + normalized lot number + expiry date. A product
    // without lot tracking has one no-lot bucket (no lot number, no expiry).
    internal sealed record LotKey(Guid StoreProductId, string? LotNumber, DateOnly? ExpiryDate);

    public async Task ConfirmAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var now = clock.UtcNow;
        var today = BusinessCalendar.Today(now);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);

        await using var transaction = await context.BeginTransactionAsync(cancellationToken);
        await locks.LockGoodsReceiptAsync(receiptId, cancellationToken);

        var receipt = await context.GoodsReceipts
            .Include(r => r.Supplier)
            .Include(r => r.Items).ThenInclude(i => i.StoreProduct).ThenInclude(sp => sp.Product)
            .Include(r => r.Items).ThenInclude(i => i.ProductPackaging)
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Goods receipt", receiptId);

        if (receipt.Status != GoodsReceiptStatus.Draft)
        {
            throw new BusinessRuleException(
                $"Goods receipt '{receipt.ReceiptNumber}' is {EnumText.Format(receipt.Status)}; only DRAFT receipts can be confirmed.");
        }

        if (!receipt.Supplier.IsActive)
        {
            throw new BusinessRuleException("The supplier is not active.");
        }

        var items = receipt.Items.OrderBy(i => i.Id).ToList();
        if (items.Count == 0)
        {
            throw new BusinessRuleException($"Goods receipt '{receipt.ReceiptNumber}' has no items to confirm.");
        }

        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            var violation = ReceiptItemRules.GetViolation(
                item.StoreProduct.Product, item.ProductPackaging, item.SupplierLotNumber,
                item.ManufacturingDate, item.ExpiryDate, today);

            if (violation is not null)
            {
                throw new BusinessRuleException($"Line {index + 1}: {violation}");
            }
        }

        var lots = await ResolveLotsAsync(items, cancellationToken);

        var movement = new StockMovement(
            storeId,
            await DocumentNumbers.NextMovementNumberAsync(context, storeId, today, cancellationToken),
            StockMovementType.StockIn,
            receipt.ReceivedAt,
            actorId,
            goodsReceiptId: receipt.Id);

        var lotIdsByItem = new Dictionary<Guid, Guid>();
        foreach (var item in items)
        {
            var lot = lots[KeyOf(item)];
            var change = lot.ReceiveStock(item.BaseQuantity, item.BaseUnitCost);

            movement.AddItem(lot.Id, change, item.Note);
            lotIdsByItem[item.Id] = lot.Id;
        }

        receipt.Confirm(actorId, now, lotIdsByItem);
        movement.Post(actorId, now);
        context.StockMovements.Add(movement);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (databaseErrors.IsUniqueViolation(exception))
        {
            // Another receipt created the same logical lot at the same moment.
            throw new ConflictException("The inventory lot was created by another request; please try again.");
        }
    }

    internal static LotKey KeyOf(AgriSage.Domain.Features.GoodsReceipts.Entities.GoodsReceiptItem item) =>
        item.StoreProduct.Product.RequiresLotTracking
            ? new LotKey(item.StoreProductId, item.SupplierLotNumber!.Trim().ToLowerInvariant(), item.ExpiryDate)
            : new LotKey(item.StoreProductId, null, null);

    private async Task<Dictionary<LotKey, InventoryLot>> ResolveLotsAsync(
        IReadOnlyList<AgriSage.Domain.Features.GoodsReceipts.Entities.GoodsReceiptItem> items,
        CancellationToken cancellationToken)
    {
        var lots = new Dictionary<LotKey, InventoryLot>();

        foreach (var group in items.GroupBy(KeyOf))
        {
            var key = group.Key;
            var first = group.First();

            var lot = key.LotNumber is null
                ? await context.InventoryLots.Include(l => l.Balance)
                    .FirstOrDefaultAsync(l => l.StoreProductId == key.StoreProductId && l.LotNumber == null, cancellationToken)
                : await context.InventoryLots.Include(l => l.Balance)
                    .FirstOrDefaultAsync(
                        l => l.StoreProductId == key.StoreProductId
                             && l.LotNumber != null && l.LotNumber.ToLower() == key.LotNumber
                             && l.ExpiryDate == key.ExpiryDate,
                        cancellationToken);

            if (lot is null)
            {
                lot = new InventoryLot(
                    key.StoreProductId,
                    key.LotNumber is null ? null : first.SupplierLotNumber!.Trim(),
                    group.Select(i => i.ManufacturingDate).FirstOrDefault(d => d is not null),
                    key.ExpiryDate);
                context.InventoryLots.Add(lot);
            }
            else if (lot.Status == InventoryLotStatus.Depleted)
            {
                // Stock is coming in again. Quarantined, blocked or expired lots keep their status: the goods are
                // received but stay unsellable until staff decide.
                lot.ChangeStatus(InventoryLotStatus.Active);
            }

            lots[key] = lot;
        }

        return lots;
    }
}
