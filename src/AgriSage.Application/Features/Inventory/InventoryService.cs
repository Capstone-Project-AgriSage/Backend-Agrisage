using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Inventory.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Inventory;

// Read side of lots and stock movements, plus the manual lot status change.
public sealed partial class InventoryService(IAgriSageDbContext context, IDateTimeProvider clock, IRowLockService locks) : IInventoryService
{
    private sealed record LotRow(
        Guid Id, Guid StoreProductId, Guid ProductId, string Sku, string ProductName, string? LotNumber,
        DateOnly? ManufacturingDate, DateOnly? ExpiryDate, InventoryLotStatus Status,
        long QuantityOnHand, long QuantityReserved, decimal TotalCostValue);

    public async Task<PagedResult<InventoryLotResponse>> ListLotsAsync(InventoryLotListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.InventoryLots.AsNoTracking().Where(l => l.StoreProduct.StoreId == storeId);

        if (request.StoreProductId is not null)
        {
            query = query.Where(l => l.StoreProductId == request.StoreProductId);
        }

        if (EnumText.TryParse<InventoryLotStatus>(request.Status, out var status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (request.ExpiringBefore is { } before)
        {
            query = query.Where(l => l.ExpiryDate != null && l.ExpiryDate <= before);
        }

        if (request.HasStock is { } hasStock)
        {
            query = hasStock
                ? query.Where(l => l.Balance.QuantityOnHand > 0)
                : query.Where(l => l.Balance.QuantityOnHand == 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(l => l.StoreProduct.Product.Name.ToLower().Contains(term)
                || l.StoreProduct.Product.Sku.ToLower().Contains(term)
                || (l.LotNumber != null && l.LotNumber.ToLower().Contains(term)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await Project(query
                .OrderBy(l => l.ExpiryDate == null).ThenBy(l => l.ExpiryDate).ThenBy(l => l.LotNumber).ThenBy(l => l.Id)
                .Skip(request.Skip).Take(request.PageSize))
            .ToListAsync(cancellationToken);

        var today = BusinessCalendar.Today(clock.UtcNow);

        return new PagedResult<InventoryLotResponse>(rows.Select(r => ToResponse(r, today)).ToList(), request.Page, request.PageSize, total);
    }

    public async Task<InventoryLotResponse> GetLotAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var row = await Project(context.InventoryLots.AsNoTracking().Where(l => l.StoreProduct.StoreId == storeId && l.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Inventory lot", id);

        return ToResponse(row, BusinessCalendar.Today(clock.UtcNow));
    }

    public async Task<InventoryLotResponse> ChangeLotStatusAsync(Guid id, ChangeLotStatusRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var lot = await context.InventoryLots.FirstOrDefaultAsync(l => l.Id == id && l.StoreProduct.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Inventory lot", id);

        EnumText.TryParse<InventoryLotStatus>(request.Status, out var status);
        if (status == InventoryLotStatus.Active && lot.ExpiryDate < BusinessCalendar.Today(clock.UtcNow))
        {
            throw new BusinessRuleException("An expired lot cannot be set to ACTIVE.");
        }

        lot.ChangeStatus(status);
        await context.SaveChangesAsync(cancellationToken);

        return await GetLotAsync(id, cancellationToken);
    }

    public async Task<PagedResult<StockMovementListItem>> ListMovementsAsync(StockMovementListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.StockMovements.AsNoTracking().Where(m => m.StoreId == storeId);

        if (EnumText.TryParse<StockMovementType>(request.Type, out var type))
        {
            query = query.Where(m => m.MovementType == type);
        }

        if (request.GoodsReceiptId is not null)
        {
            query = query.Where(m => m.GoodsReceiptId == request.GoodsReceiptId);
        }

        if (request.FromDate is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(m => m.OccurredAt >= start);
        }

        if (request.ToDate is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(m => m.OccurredAt < end);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(m => new
            {
                m.Id,
                m.MovementNumber,
                m.MovementType,
                m.Status,
                m.OccurredAt,
                m.GoodsReceiptId,
                ItemCount = m.Items.Count
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(m => new StockMovementListItem(
            m.Id, m.MovementNumber, EnumText.Format(m.MovementType), EnumText.Format(m.Status), m.OccurredAt,
            m.GoodsReceiptId, m.ItemCount)).ToList();

        return new PagedResult<StockMovementListItem>(items, request.Page, request.PageSize, total);
    }

    public async Task<StockMovementResponse> GetMovementAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var movement = await context.StockMovements.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.StoreId == storeId, cancellationToken)
            ?? throw new NotFoundException("Stock movement", id);

        var items = await (
            from item in context.StockMovementItems.AsNoTracking()
            where item.StockMovementId == id
            join lot in context.InventoryLots.IgnoreQueryFilters().AsNoTracking() on item.InventoryLotId equals lot.Id
            orderby item.Id
            select new StockMovementItemResponse(
                item.Id, lot.Id, lot.LotNumber, lot.ExpiryDate, lot.StoreProduct.Product.Sku, lot.StoreProduct.Product.Name,
                item.QuantityDeltaBase, item.UnitCostSnapshot, item.TotalCostSnapshot, item.QuantityOnHandAfter,
                item.TotalCostValueAfter, item.Note))
            .ToListAsync(cancellationToken);

        return new StockMovementResponse(
            movement.Id, movement.MovementNumber, EnumText.Format(movement.MovementType), EnumText.Format(movement.Status),
            movement.OccurredAt, movement.GoodsReceiptId, movement.OrderId, movement.DeliveryId, movement.StocktakeId,
            movement.ReversalOfMovementId, movement.ReasonCode, movement.Reason, movement.CreatedBy, movement.PostedAt,
            movement.PostedBy, items);
    }

    private static IQueryable<LotRow> Project(IQueryable<AgriSage.Domain.Features.Inventory.Entities.InventoryLot> query) =>
        query.Select(l => new LotRow(
            l.Id, l.StoreProductId, l.StoreProduct.ProductId, l.StoreProduct.Product.Sku, l.StoreProduct.Product.Name,
            l.LotNumber, l.ManufacturingDate, l.ExpiryDate, l.Status,
            l.Balance.QuantityOnHand, l.Balance.QuantityReserved, l.Balance.TotalCostValue));

    private static InventoryLotResponse ToResponse(LotRow r, DateOnly today) => new(
        r.Id, r.StoreProductId, r.ProductId, r.Sku, r.ProductName, r.LotNumber, r.ManufacturingDate, r.ExpiryDate,
        r.ExpiryDate < today, EnumText.Format(r.Status), r.QuantityOnHand, r.QuantityReserved,
        r.QuantityOnHand - r.QuantityReserved,
        r.QuantityOnHand > 0 ? CostRounding.RoundUnitCost(r.TotalCostValue / r.QuantityOnHand) : null,
        r.TotalCostValue);
}
