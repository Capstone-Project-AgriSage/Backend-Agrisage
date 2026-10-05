using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Returns;

public sealed class SalesReturnQueries(IAgriSageDbContext context, ReturnSources sources)
{
    public async Task<Order> OrderAsync(Guid id, Guid storeId, Guid? farmerId, bool tracked, CancellationToken token)
    {
        var query = context.Orders.Include(o => o.Items).Where(o => o.Id == id && o.StoreId == storeId);
        if (farmerId is { } farmer) query = query.Where(o => o.FarmerProfileId == farmer);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Order", id);
    }

    public async Task<ReturnableResponse> ReturnableAsync(Guid id, Guid? farmerId, CancellationToken token)
    {
        var order = await OrderAsync(id, await ActiveStore.GetIdAsync(context, token), farmerId, false, token);
        var rows = await sources.LoadAsync(order, token);
        var returned = await sources.ReturnedByItemAsync(id, null, token);
        var items = order.Items.Where(i => !i.IsDeleted).Select(i =>
        {
            var used = returned.GetValueOrDefault(i.Id);
            var available = Math.Max(0, i.FulfilledBaseQuantity - used);
            return new ReturnableOrderItem(i.Id, i.ProductSkuSnapshot, i.ProductNameSnapshot,
                i.FulfilledBaseQuantity, used, available, i.UnitPrice, i.ConversionToBaseSnapshot,
                rows.Where(s => s.OrderItemId == i.Id).OrderBy(s => s.LotId).ThenBy(s => s.MovementItemId).Select(s =>
                    new ReturnableSource(s.DeliveryItemId, s.AllocationId, s.MovementItemId, s.LotId, s.LotNumber, s.ExpiryDate,
                        s.Fulfilled, s.Returned, Math.Min(available, Math.Max(0, s.Fulfilled - s.Returned)), i.UnitPrice, i.ConversionToBaseSnapshot, s.Cost)).ToList());
        }).OrderBy(i => i.Sku).ThenBy(i => i.OrderItemId).ToList();
        return new(order.Id, order.OrderNumber, EnumText.Format(order.FulfillmentType), items);
    }

    public async Task<PagedResult<SalesReturnListItem>> ListAsync(SalesReturnListRequest request, Guid? farmerId, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var query = from r in context.SalesReturns.AsNoTracking()
                    join o in context.Orders.IgnoreQueryFilters().AsNoTracking() on r.OrderId equals o.Id
                    where r.DeletedAt == null && r.StoreId == storeId
                    select new { Return = r, o.OrderNumber, o.CustomerNameSnapshot };
        if (farmerId is { } own) query = query.Where(x => x.Return.FarmerProfileId == own);
        if (request.FarmerProfileId is { } farmer) query = query.Where(x => x.Return.FarmerProfileId == farmer);
        if (request.OrderId is { } order) query = query.Where(x => x.Return.OrderId == order);
        if (EnumText.TryParse<SalesReturnStatus>(request.Status, out var status)) query = query.Where(x => x.Return.Status == status);
        if (request.FromDate is { } from) { var start = BusinessCalendar.StartOfDay(from); query = query.Where(x => x.Return.RequestedAt >= start); }
        if (request.ToDate is { } to) { var end = BusinessCalendar.StartOfDay(to.AddDays(1)); query = query.Where(x => x.Return.RequestedAt < end); }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Return.ReturnNumber.ToLower().Contains(term) || x.OrderNumber.ToLower().Contains(term)
                || x.CustomerNameSnapshot.ToLower().Contains(term));
        }
        var total = await query.LongCountAsync(token);
        var rows = await query.OrderByDescending(x => x.Return.RequestedAt).ThenBy(x => x.Return.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                x.Return.Id,
                x.Return.ReturnNumber,
                x.Return.OrderId,
                x.OrderNumber,
                x.Return.FarmerProfileId,
                x.CustomerNameSnapshot,
                x.Return.Status,
                x.Return.TotalReturnAmount,
                x.Return.TotalRefundAmount,
                x.Return.RequestedAt
            }).ToListAsync(token);
        return new(rows.Select(x => new SalesReturnListItem(x.Id, x.ReturnNumber, x.OrderId, x.OrderNumber, x.FarmerProfileId,
            x.CustomerNameSnapshot, EnumText.Format(x.Status), x.TotalReturnAmount, x.TotalRefundAmount, x.RequestedAt)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<SalesReturnResponse> GetAsync(Guid id, Guid? farmerId, CancellationToken token)
    {
        var storeId = await ActiveStore.GetIdAsync(context, token);
        var header = await context.SalesReturns.AsNoTracking().Where(r => r.Id == id && r.StoreId == storeId
            && (farmerId == null || r.FarmerProfileId == farmerId)).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Sales return", id);
        var order = await context.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == header.OrderId)
            .Select(o => new { o.OrderNumber, o.CustomerNameSnapshot }).SingleAsync(token);
        var items = await context.SalesReturnItems.AsNoTracking().Where(i => i.SalesReturnId == id).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                i.OrderItemId,
                i.DeliveryItemId,
                i.DeliveryItemLotAllocationId,
                i.OriginalStockMovementItemId,
                i.InventoryLotId,
                i.ReturnedBaseQuantity,
                i.SellingUnitPriceSnapshot,
                i.ConversionToBaseSnapshot,
                i.ReturnValue,
                i.OriginalCogsUnitCost,
                i.ReturnInventoryCostValue,
                i.ReasonCode,
                i.ConditionStatus,
                i.InventoryDisposition,
                i.InspectionNote,
                i.ReturnStockMovementId,
                i.DebtAdjustmentTransactionId
            }).ToListAsync(token);
        var refunds = await context.Refunds.AsNoTracking().Where(r => r.SalesReturnId == id).OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).ToListAsync(token);
        return new(header.Id, header.StoreId, header.ReturnNumber, header.OrderId, order.OrderNumber, header.FarmerProfileId,
            order.CustomerNameSnapshot, EnumText.Format(header.Status), header.RequestedBy, header.RequestedAt, header.ApprovedBy,
            header.ApprovedAt, header.ReceivedBy, header.ReceivedAt, header.InspectedBy, header.InspectedAt, header.CompletedAt,
            header.CancelledBy, header.CancelledAt, header.CancelReason, header.ReasonSummary, header.Note,
            header.TotalReturnAmount, header.TotalDebtAdjustment, header.TotalRefundAmount,
            items.Select(i => new SalesReturnItemResponse(i.Id, i.OrderItemId, i.DeliveryItemId, i.DeliveryItemLotAllocationId,
                i.OriginalStockMovementItemId, i.InventoryLotId, i.ReturnedBaseQuantity, i.SellingUnitPriceSnapshot,
                i.ConversionToBaseSnapshot, i.ReturnValue, i.OriginalCogsUnitCost, i.ReturnInventoryCostValue, i.ReasonCode,
                EnumText.Format(i.ConditionStatus), EnumText.Format(i.InventoryDisposition), i.InspectionNote,
                i.ReturnStockMovementId, i.DebtAdjustmentTransactionId)).ToList(), refunds.Select(RefundResponse.From).ToList());
    }
}
