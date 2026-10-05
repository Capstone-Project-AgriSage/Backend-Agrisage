using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

public sealed class StockAdjustmentService(IAgriSageDbContext context, IRowLockService locks,
    ICurrentUserService currentUser, StockAdjustmentPosting posting, IInventoryService inventory) : IStockAdjustmentService
{
    public async Task<StockMovementResponse> CreateAsync(StockAdjustmentRequest request, CancellationToken token)
    {
        var actor = InventoryActors.Require(currentUser, manage: true);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        var ids = request.Lines.Select(l => l.InventoryLotId).ToArray();
        await locks.LockLotBalancesAsync(ids, token);
        var lots = await context.InventoryLots.Include(l => l.Balance)
            .Where(l => ids.Contains(l.Id) && l.StoreProduct.StoreId == storeId).ToDictionaryAsync(l => l.Id, token);
        foreach (var id in ids)
        {
            if (!lots.ContainsKey(id))
            {
                throw new NotFoundException("Inventory lot", id);
            }
        }

        var lines = request.Lines.Select(l => new StockAdjustmentPosting.Line(lots[l.InventoryLotId], l.QuantityDeltaBase, l.UnitCost)).ToList();
        var movements = await posting.PostAsync(storeId, actor, lines, request.ReasonCode.Trim().ToUpperInvariant(), request.Note.Trim(), null, token);
        await context.SaveChangesAsync(token);
        var response = await inventory.GetMovementAsync(movements.Single().Id, token);
        await transaction.CommitAsync(token);
        return response;
    }
}
