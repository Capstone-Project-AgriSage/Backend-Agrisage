using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Returns;

// Queries the immutable SALE ledger and completed delivery attempts, never today's inventory cost.
public sealed class ReturnSources(IAgriSageDbContext context)
{
    public sealed record Source(Guid OrderItemId, Guid LotId, string? LotNumber, DateOnly? ExpiryDate,
        Guid? DeliveryItemId, Guid? AllocationId, Guid? MovementItemId, Guid? MovementId,
        long Fulfilled, long Returned, decimal Cost);

    public async Task<IReadOnlyList<Source>> LoadAsync(Order order, CancellationToken token, Guid? exceptReturnId = null)
    {
        var returned = await (from i in context.SalesReturnItems.AsNoTracking()
                              join r in context.SalesReturns.AsNoTracking() on i.SalesReturnId equals r.Id
                              where r.OrderId == order.Id && r.Id != exceptReturnId && r.Status != SalesReturnStatus.Rejected && r.Status != SalesReturnStatus.Cancelled
                              select new { i.OrderItemId, i.DeliveryItemLotAllocationId, i.OriginalStockMovementItemId, i.ReturnedBaseQuantity })
            .ToListAsync(token);
        var sales = await (from mi in context.StockMovementItems.AsNoTracking()
                           join m in context.StockMovements.AsNoTracking() on mi.StockMovementId equals m.Id
                           join lot in context.InventoryLots.IgnoreQueryFilters().AsNoTracking() on mi.InventoryLotId equals lot.Id
                           where mi.DeletedAt == null && m.DeletedAt == null && m.OrderId == order.Id && m.StoreId == order.StoreId
                               && m.MovementType == StockMovementType.Sale && m.Status == StockMovementStatus.Posted && mi.QuantityDeltaBase < 0
                           select new
                           {
                               mi.Id,
                               mi.StockMovementId,
                               mi.InventoryLotId,
                               lot.StoreProductId,
                               lot.LotNumber,
                               lot.ExpiryDate,
                               Quantity = -mi.QuantityDeltaBase,
                               mi.UnitCostSnapshot
                           }).ToListAsync(token);
        var result = new List<Source>();
        if (order.FulfillmentType == FulfillmentType.Pickup)
        {
            foreach (var item in order.Items.Where(i => !i.IsDeleted && i.FulfilledBaseQuantity > 0))
                foreach (var sale in sales.Where(s => s.StoreProductId == item.StoreProductId))
                {
                    // A movement item has no order_item_id. Its lot/product plus the order identify the valid source;
                    // enforce both the source's shared capacity and the order item's capacity when accepting returns.
                    var used = returned.Where(r => r.OriginalStockMovementItemId == sale.Id).Sum(r => r.ReturnedBaseQuantity);
                    result.Add(new(item.Id, sale.InventoryLotId, sale.LotNumber, sale.ExpiryDate, null, null,
                        sale.Id, sale.StockMovementId, sale.Quantity, used, sale.UnitCostSnapshot));
                }
            return result;
        }

        var allocations = await (from a in context.DeliveryItemLotAllocations.AsNoTracking()
                                 join di in context.DeliveryItems.AsNoTracking() on a.DeliveryItemId equals di.Id
                                 join d in context.Deliveries.AsNoTracking() on di.DeliveryId equals d.Id
                                 join lot in context.InventoryLots.IgnoreQueryFilters().AsNoTracking() on a.InventoryLotId equals lot.Id
                                 where a.DeletedAt == null && di.DeletedAt == null && d.DeletedAt == null
                                     && d.OrderId == order.Id && d.StoreId == order.StoreId && a.DeliveredBaseQuantity > 0
                                 select new
                                 {
                                     a.Id,
                                     di.OrderItemId,
                                     a.DeliveryItemId,
                                     a.InventoryLotId,
                                     a.DeliveredBaseQuantity,
                                     lot.LotNumber,
                                     lot.ExpiryDate
                                 }).ToListAsync(token);
        var allocationIds = allocations.Select(a => a.Id).ToArray();
        var attempts = await (from ai in context.DeliveryAttemptItems.AsNoTracking()
                              join a in context.DeliveryAttempts.AsNoTracking() on ai.DeliveryAttemptId equals a.Id
                              where allocationIds.Contains(ai.DeliveryItemLotAllocationId) && ai.DeliveredBaseQuantity > 0 && a.SaleStockMovementId != null
                              select new { ai.DeliveryItemLotAllocationId, ai.DeliveredBaseQuantity, a.SaleStockMovementId }).ToListAsync(token);
        foreach (var allocation in allocations)
        {
            var layers = attempts.Where(a => a.DeliveryItemLotAllocationId == allocation.Id).Select(a =>
            {
                var entries = sales.Where(s => s.StockMovementId == a.SaleStockMovementId && s.InventoryLotId == allocation.InventoryLotId).ToList();
                return new
                {
                    a.DeliveredBaseQuantity,
                    a.SaleStockMovementId,
                    Cost = entries.Count == 0 ? (decimal?)null : entries.Sum(s => s.Quantity * s.UnitCostSnapshot) / entries.Sum(s => s.Quantity)
                };
            }).ToList();
            if (layers.Count == 0 || layers.Any(l => l.Cost is null)
                || layers.Sum(l => l.DeliveredBaseQuantity) != allocation.DeliveredBaseQuantity)
                throw new BusinessRuleException($"The original SALE cost ledger is incomplete for allocation {allocation.Id}.");
            var cost = CostRounding.RoundUnitCost(layers.Sum(l => l.DeliveredBaseQuantity * l.Cost!.Value) / allocation.DeliveredBaseQuantity);
            var movementIds = layers.Select(l => l.SaleStockMovementId).Distinct().ToList();
            result.Add(new(allocation.OrderItemId, allocation.InventoryLotId, allocation.LotNumber, allocation.ExpiryDate,
                allocation.DeliveryItemId, allocation.Id, null, movementIds.Count == 1 ? movementIds[0] : null,
                allocation.DeliveredBaseQuantity, returned.Where(r => r.DeliveryItemLotAllocationId == allocation.Id).Sum(r => r.ReturnedBaseQuantity), cost));
        }
        return result;
    }

    public async Task<Dictionary<Guid, long>> ReturnedByItemAsync(Guid orderId, Guid? exceptReturnId, CancellationToken token) =>
        await (from i in context.SalesReturnItems.AsNoTracking()
               join r in context.SalesReturns.AsNoTracking() on i.SalesReturnId equals r.Id
               where r.OrderId == orderId && r.Id != exceptReturnId && r.Status != SalesReturnStatus.Rejected && r.Status != SalesReturnStatus.Cancelled
               group i by i.OrderItemId into g
               select new { Id = g.Key, Quantity = g.Sum(i => i.ReturnedBaseQuantity) }).ToDictionaryAsync(r => r.Id, r => r.Quantity, token);
}
