using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Returns;

// All mutations serialize on the original order, then the return. The order is read but never rewritten.
public sealed class SalesReturnService(IAgriSageDbContext context, IRowLockService locks, ICurrentUserService currentUser,
    IDateTimeProvider clock, AuditTrail audit, SalesReturnQueries queries, ReturnSources sources,
    IDebtReturnPosting debtPosting, AgriSage.Application.Features.Permissions.IPermissionEvaluator? permissions = null) : ISalesReturnService
{
    public Task<ReturnableResponse> ReturnableAsync(Guid orderId, CancellationToken token) => queries.ReturnableAsync(orderId, null, token);
    public Task<PagedResult<SalesReturnListItem>> ListAsync(SalesReturnListRequest request, CancellationToken token) => queries.ListAsync(request, null, token);
    public Task<SalesReturnResponse> GetAsync(Guid id, CancellationToken token) => queries.GetAsync(id, null, token);
    public Task<SalesReturnResponse> CreateAsync(CreateReturnRequest request, CancellationToken token) => CreateForAsync(request, null, token);

    internal async Task<SalesReturnResponse> CreateForAsync(CreateReturnRequest request, Guid? farmerId, CancellationToken token)
    {
        var actor = await ActorAsync(farmerId, false, null, token);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        await locks.LockOrderAsync(request.OrderId, token);
        var order = await queries.OrderAsync(request.OrderId, storeId, farmerId, false, token);
        var salesReturn = new SalesReturn(order,
            await DocumentNumbers.NextAsync(context.SalesReturns.IgnoreQueryFilters().AsNoTracking().Where(r => r.StoreId == storeId)
                .Select(r => r.ReturnNumber), DocumentNumbers.SalesReturn, BusinessCalendar.Today(clock.UtcNow), token),
            actor, clock.UtcNow, Texts.Clean(request.ReasonSummary), Texts.Clean(request.Note));
        await AddLinesAsync(salesReturn, order, request.Items, token);
        context.SalesReturns.Add(salesReturn);
        audit.Record("RETURN_REQUESTED", "SALES_RETURN", salesReturn.Id, storeId);
        await context.SaveChangesAsync(token);
        var response = await queries.GetAsync(salesReturn.Id, farmerId, token);
        await transaction.CommitAsync(token);
        return response;
    }

    public Task<SalesReturnResponse> AddItemAsync(Guid id, ReturnItemRequest request, CancellationToken token) =>
        MutateAsync(id, null, false, async (r, order, _) => await AddLinesAsync(r, order, [request], token), token);

    public Task<SalesReturnResponse> RemoveItemAsync(Guid id, Guid itemId, CancellationToken token) =>
        MutateAsync(id, null, false, (r, _, actor) =>
        {
            RequireItem(r, itemId);
            r.RemoveItem(itemId, actor, clock.UtcNow);
            return Task.CompletedTask;
        }, token);

    public Task<SalesReturnResponse> ApproveAsync(Guid id, CancellationToken token) => MutateAsync(id, null, true, (r, _, actor) =>
    {
        r.Approve(actor, clock.UtcNow);
        audit.Record("RETURN_APPROVED", "SALES_RETURN", r.Id, r.StoreId);
        return Task.CompletedTask;
    }, token);
    public Task<SalesReturnResponse> RejectAsync(Guid id, ReturnReasonRequest request, CancellationToken token) =>
        MutateAsync(id, null, true, (r, _, _) =>
        {
            r.Reject();
            audit.Record("RETURN_REJECTED", "SALES_RETURN", r.Id, r.StoreId, reason: request.Reason.Trim());
            return Task.CompletedTask;
        }, token);

    public Task<SalesReturnResponse> CancelAsync(Guid id, ReturnReasonRequest request, CancellationToken token) => CancelForAsync(id, request, null, token);
    internal Task<SalesReturnResponse> CancelForAsync(Guid id, ReturnReasonRequest request, Guid? farmerId, CancellationToken token) =>
        MutateAsync(id, farmerId, false, (r, _, actor) =>
        {
            if (farmerId is not null && r.Status != SalesReturnStatus.Requested)
                throw new BusinessRuleException("A customer can cancel only a REQUESTED return.");
            r.Cancel(actor, clock.UtcNow, request.Reason.Trim());
            audit.Record("RETURN_CANCELLED", "SALES_RETURN", r.Id, r.StoreId, reason: request.Reason.Trim());
            return Task.CompletedTask;
        }, token);
    public Task<SalesReturnResponse> ReceiveAsync(Guid id, CancellationToken token) => MutateAsync(id, null, false, (r, _, actor) =>
    {
        r.MarkReceived(actor, clock.UtcNow);
        return Task.CompletedTask;
    }, token);
    public Task<SalesReturnResponse> InspectAsync(Guid id, Guid itemId, ReturnInspectionRequest request, CancellationToken token) =>
        MutateAsync(id, null, false, (r, _, _) =>
        {
            RequireItem(r, itemId);
            EnumText.TryParse<ReturnConditionStatus>(request.ConditionStatus, out var condition);
            r.InspectItem(itemId, condition, Texts.Clean(request.InspectionNote));
            return Task.CompletedTask;
        }, token);

    public Task<SalesReturnResponse> CompleteInspectionAsync(Guid id, CancellationToken token) =>
        MutateAsync(id, null, true, async (r, order, actor) =>
        {
            if (r.Status != SalesReturnStatus.Received)
                throw new BusinessRuleException("Only a RECEIVED return can complete inspection.");
            var items = r.Items.Where(i => !i.IsDeleted).OrderBy(i => i.Id).ToList();
            var uninspected = items.Where(i => !i.IsInspected).ToDictionary(i => i.Id.ToString(), _ => new[] { "Inspect this return line first." });
            if (uninspected.Count > 0) throw new BusinessRuleException("Every return line must be inspected.", uninspected);
            var restock = items.Where(i => i.InventoryDisposition == InventoryDisposition.Restock).ToList();
            if (restock.Any(i => i.OriginalCogsUnitCost is null)) throw new BusinessRuleException("The original cost is required to restock returned goods.");
            if (r.FarmerProfileId is { } farmer) await locks.LockFarmerProfileAsync(farmer, token);
            var lotIds = restock.Select(i => i.InventoryLotId).Distinct().ToArray();
            await locks.LockLotBalancesAsync(lotIds, token);
            var lots = await context.InventoryLots.Include(l => l.Balance).Where(l => lotIds.Contains(l.Id) && l.StoreProduct.StoreId == r.StoreId)
                .ToDictionaryAsync(l => l.Id, token);
            if (lots.Count != lotIds.Length) throw new BusinessRuleException("An original lot is no longer available for restocking.");
            StockMovement? movement = null;
            if (restock.Count > 0)
            {
                movement = new StockMovement(r.StoreId,
                    await DocumentNumbers.NextMovementNumberAsync(context, r.StoreId, BusinessCalendar.Today(clock.UtcNow), token),
                    StockMovementType.ReturnIn, clock.UtcNow, actor, orderId: r.OrderId, salesReturnId: r.Id,
                    reasonCode: "SALES_RETURN", reason: r.ReasonSummary);
                foreach (var item in restock.OrderBy(i => i.InventoryLotId).ThenBy(i => i.Id))
                    movement.AddItem(item.InventoryLotId, lots[item.InventoryLotId].ReceiveStock(item.ReturnedBaseQuantity, item.OriginalCogsUnitCost!.Value));
                movement.Post(actor, clock.UtcNow);
                context.StockMovements.Add(movement);
            }
            var originalSources = await sources.LoadAsync(order, token, r.Id);
            decimal debt = 0;
            var links = new List<(Guid ItemId, Guid TransactionId)>();
            foreach (var item in items)
            {
                var source = originalSources.SingleOrDefault(s => Matches(s, item.OrderItemId, item.DeliveryItemLotAllocationId, item.OriginalStockMovementItemId));
                var before = context.DebtTransactions.Local.Select(t => t.Id).ToHashSet();
                var applied = await debtPosting.ApplyReturnAsync(r.OrderId, r.Id, item.ReturnValue, actor, source?.MovementId, token);
                if (applied < 0 || applied > item.ReturnValue) throw new BusinessRuleException("The debt posting returned an invalid adjustment amount.");
                debt += applied;
                var added = context.DebtTransactions.Local.FirstOrDefault(t => !before.Contains(t.Id) && t.SalesReturnId == r.Id);
                if (applied > 0 && added is not null) links.Add((item.Id, added.Id));
            }
            r.CompleteInspection(actor, clock.UtcNow, debt);
            if (movement is not null)
                foreach (var item in restock) r.LinkReturnStockMovement(item.Id, movement.Id);
            foreach (var link in links) r.LinkDebtAdjustmentTransaction(link.ItemId, link.TransactionId);
            r.FinishInspectionResolution(clock.UtcNow);
            audit.Record("RETURN_INSPECTION_COMPLETED", "SALES_RETURN", r.Id, r.StoreId,
                newValues: new { r.TotalReturnAmount, r.TotalDebtAdjustment, r.TotalRefundAmount, returnStockMovementId = movement?.Id });
        }, token);

    private async Task AddLinesAsync(SalesReturn r, Order order, IReadOnlyList<ReturnItemRequest> requests, CancellationToken token)
    {
        if (r.Status != SalesReturnStatus.Requested) throw new BusinessRuleException("Lines can change only on a REQUESTED return.");
        var sourceRows = await sources.LoadAsync(order, token, r.Id);
        var returned = await sources.ReturnedByItemAsync(order.Id, r.Id, token);
        foreach (var request in requests)
        {
            var item = order.Items.SingleOrDefault(i => i.Id == request.OrderItemId && !i.IsDeleted)
                ?? throw new NotFoundException("Order item", request.OrderItemId);
            var source = sourceRows.SingleOrDefault(s => Matches(s, request.OrderItemId, request.DeliveryItemLotAllocationId, request.OriginalStockMovementItemId))
                ?? throw new BusinessRuleException("The fulfillment source does not belong to this fulfilled order item.");
            var onThisReturn = r.Items.Where(i => !i.IsDeleted && i.DeliveryItemLotAllocationId == source.AllocationId
                && i.OriginalStockMovementItemId == source.MovementItemId).Sum(i => i.ReturnedBaseQuantity);
            if (request.ReturnedBaseQuantity > source.Fulfilled - source.Returned - onThisReturn)
                throw new BusinessRuleException("The returned quantity exceeds the fulfillment source's remaining quantity.");
            r.AddItem(order, item, request.ReturnedBaseQuantity, returned.GetValueOrDefault(item.Id), source.LotId,
                request.ReasonCode.Trim().ToUpperInvariant(), source.DeliveryItemId, source.AllocationId, source.MovementItemId, source.Cost);
        }
    }

    private static bool Matches(ReturnSources.Source s, Guid itemId, Guid? allocationId, Guid? movementItemId) =>
        s.OrderItemId == itemId && s.AllocationId == allocationId && s.MovementItemId == movementItemId;
    private static void RequireItem(SalesReturn r, Guid itemId)
    {
        if (!r.Items.Any(i => i.Id == itemId && !i.IsDeleted)) throw new NotFoundException("Return item", itemId);
    }
    private async Task<Guid> ActorAsync(Guid? farmerId, bool manage, string? permission, CancellationToken token)
    {
        var id = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var allowed = farmerId is not null ? currentUser.Role == "FARMER" :
            currentUser.Role is "ADMIN" or "STORE_OWNER" || (currentUser.Role == "SALES_STAFF" && (!manage || (permissions is not null && permission is not null && await permissions.HasAsync(permission, token))));
        if (!allowed) throw new ForbiddenException();
        return id;
    }
    private async Task<SalesReturnResponse> MutateAsync(Guid id, Guid? farmerId, bool manage,
        Func<SalesReturn, Order, Guid, Task> change, CancellationToken token,
        [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var permission = operation switch { "ApproveAsync" => "RETURNS.APPROVE", "RejectAsync" => "RETURNS.REJECT", "CompleteInspectionAsync" => "RETURNS.COMPLETE_INSPECTION", _ => null };
        var actor = await ActorAsync(farmerId, manage, permission, token);
        var storeId = await ActiveStore.GetIdAsync(context, token);
        await using var transaction = await context.BeginTransactionAsync(token);
        var orderId = await context.SalesReturns.AsNoTracking().Where(r => r.Id == id && r.StoreId == storeId
            && (farmerId == null || r.FarmerProfileId == farmerId)).Select(r => (Guid?)r.OrderId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Sales return", id);
        await locks.LockOrderAsync(orderId, token);
        await locks.LockSalesReturnAsync(id, token);
        var r = await context.SalesReturns.Include(r => r.Items).Include(r => r.Refunds)
            .SingleOrDefaultAsync(r => r.Id == id && r.StoreId == storeId && (farmerId == null || r.FarmerProfileId == farmerId), token)
            ?? throw new NotFoundException("Sales return", id);
        var order = await queries.OrderAsync(orderId, storeId, farmerId, false, token);
        await change(r, order, actor);
        await context.SaveChangesAsync(token);
        var response = await queries.GetAsync(id, farmerId, token);
        await transaction.CommitAsync(token);
        return response;
    }
}
