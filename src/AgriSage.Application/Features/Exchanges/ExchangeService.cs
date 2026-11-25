using AgriSage.Application.Features.Debt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Returns;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Exchanges.Entities;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Exchanges;

// Transaction owner. Reuses the return provenance/debt/stock steps and ordinary order fulfillment.
public sealed class ExchangeService(IAgriSageDbContext db, IRowLockService locks, ICurrentUserService user,
    IDateTimeProvider clock, AuditTrail audit, SalesReturnQueries returnQueries, SalesReturnService returns,
    OrderBuilder builder, OrderConfirmer confirmer, FulfillmentPostingService posting, PaymentAllocator allocator,
    IPermissionEvaluator permissions)
{
    private async Task<Guid?> AccessAsync(bool mine, string action, CancellationToken token)
    {
        if (user.UserId == null) throw new AuthenticationFailedException("Authentication is required.");
        if (mine)
        {
            if (user.Role != "FARMER" || !await permissions.HasAsync("MY_EXCHANGES." + action, token)) throw new ForbiddenException();
            return await db.FarmerProfiles.AsNoTracking().Where(f => f.UserId == user.UserId).Select(f => (Guid?)f.Id)
                .SingleOrDefaultAsync(token) ?? throw new ForbiddenException("No customer profile.");
        }
        if (user.Role is not ("ADMIN" or "STORE_OWNER" or "SALES_STAFF") || !await permissions.HasAsync("EXCHANGES." + action, token))
            throw new ForbiddenException();
        return null;
    }
    public async Task<ReturnableResponse> ExchangeableAsync(Guid orderId, bool mine, CancellationToken token)
    {
        var farmer = await AccessAsync(mine, "READ", token);
        var order = await returnQueries.OrderAsync(orderId, await ActiveStore.GetIdAsync(db, token), farmer, false, token);
        RequireCompleted(order);
        var result = await returnQueries.ReturnableAsync(orderId, farmer, token);
        return mine ? FarmerReturnView.Redact(result) : result;
    }
    public async Task<ExchangeResponse> CreateAsync(CreateExchangeRequest request, bool mine, CancellationToken token)
    {
        var farmer = await AccessAsync(mine, "CREATE", token); var store = await ActiveStore.GetIdAsync(db, token);
        var actor = user.UserId!.Value;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var tx = await db.BeginTransactionAsync(token);
        await locks.LockOrderAsync(request.OrderId, token);
        var existing = await db.SalesExchanges.AsNoTracking().SingleOrDefaultAsync(e => e.StoreId == store && e.RequestedBy == actor && e.RequestKey == request.RequestKey, token);
        if (existing != null)
        {
            if (existing.RequestHash != hash) throw new ConflictException("This request key has already been used for a different exchange.");
            await tx.CommitAsync(token); return await DetailAsync(existing.Id, farmer, token);
        }
        var order = await returnQueries.OrderAsync(request.OrderId, store, farmer, false, token); RequireCompleted(order);
        var returned = await returns.CreateCoreAsync(order, new(request.OrderId, request.Items, request.Reason), actor, token);
        var replacement = await builder.BuildAsync(Draft(order, request.Replacements.Select(i => new OrderItemRequest(i.StoreProductId, i.ProductPackagingId, i.Quantity)).ToList()), token);
        var exchange = new SalesExchange(returned, request.RequestKey, hash, actor, request.Reason.Trim());
        foreach (var item in replacement.Items) exchange.AddReplacement(item);
        await CheckStockAsync(exchange, token);
        db.SalesReturns.Add(returned); db.SalesExchanges.Add(exchange);
        Record(exchange, "REQUESTED"); await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return await DetailAsync(exchange.Id, farmer, token);
    }
    public async Task<ExchangeListResponse> ListAsync(ExchangeListRequest request, bool mine, CancellationToken token)
    {
        var farmer = await AccessAsync(mine, "READ", token); var store = await ActiveStore.GetIdAsync(db, token);
        var query = from e in db.SalesExchanges.AsNoTracking()
                    join o in db.Orders.AsNoTracking() on e.OrderId equals o.Id
                    join r in db.SalesReturns.AsNoTracking() on e.SalesReturnId equals r.Id
                    where e.StoreId == store && (farmer == null || e.FarmerProfileId == farmer)
                    select new { E = e, o.OrderNumber, o.CustomerNameSnapshot, r.ReturnNumber };
        if (request.OrderId is {} orderId) query = query.Where(x => x.E.OrderId == orderId);
        if (EnumText.TryParse<ExchangeStatus>(request.Status, out var status)) query = query.Where(x => x.E.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x => x.OrderNumber.ToLower().Contains(search) || x.ReturnNumber.ToLower().Contains(search) || x.CustomerNameSnapshot.ToLower().Contains(search));
        }
        // Aggregate the same authorized/filter scope before paging. Exchange credit is noncash;
        // only the confirmed signed difference of a completed exchange is a receipt/refund.
        var totals = await query.GroupBy(_ => 1).Select(group => new
        {
            Count = group.LongCount(),
            Received = group.Sum(x => x.E.Status == ExchangeStatus.Completed && x.E.Difference > 0 ? x.E.Difference!.Value : 0m),
            Refunded = group.Sum(x => x.E.Status == ExchangeStatus.Completed && x.E.Difference < 0 ? -x.E.Difference!.Value : 0m)
        }).SingleOrDefaultAsync(token);
        var received = totals?.Received ?? 0m;
        var refunded = totals?.Refunded ?? 0m;
        var rows = await query.OrderByDescending(x => x.E.CreatedAt).ThenBy(x => x.E.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(x => new { x.E.Id, x.E.OrderId, x.OrderNumber, x.ReturnNumber, x.CustomerNameSnapshot, x.E.Status, x.E.ReplacementAmount, x.E.Difference, x.E.CreatedAt }).ToListAsync(token);
        return new(rows.Select(x => new ExchangeListItem(x.Id, x.OrderId, x.OrderNumber, x.ReturnNumber, x.CustomerNameSnapshot,
            EnumText.Format(x.Status), x.ReplacementAmount, x.Difference, x.CreatedAt)).ToList(), request.Page, request.PageSize,
            totals?.Count ?? 0, new(received, refunded, received - refunded));
    }
    public async Task<ExchangeResponse> GetAsync(Guid id, bool mine, CancellationToken token) =>
        await DetailAsync(id, await AccessAsync(mine, "READ", token), token);

    public Task<ExchangeResponse> ApproveAsync(Guid id, CancellationToken token) => ChangeAsync(id, false, "APPROVE", async (e, r, _, actor) =>
    { await CheckStockAsync(e, token); e.Approve(); r.Approve(actor, clock.UtcNow); }, token);
    public Task<ExchangeResponse> RejectAsync(Guid id, ReturnReasonRequest request, CancellationToken token) => ChangeAsync(id, false, "REJECT", (e, r, _, _) =>
    { e.Reject(); r.Reject(); return Task.CompletedTask; }, token, request.Reason);
    public Task<ExchangeResponse> CancelAsync(Guid id, ReturnReasonRequest request, bool mine, CancellationToken token) => ChangeAsync(id, mine, "CANCEL", (e, r, _, actor) =>
    {
        if (mine && e.Status != ExchangeStatus.Requested) throw new BusinessRuleException("Only a requested exchange may be cancelled by its customer.");
        e.Cancel(); r.Cancel(actor, clock.UtcNow, request.Reason.Trim()); return Task.CompletedTask;
    }, token, request.Reason);
    public Task<ExchangeResponse> ReceiveAsync(Guid id, CancellationToken token) => ChangeAsync(id, false, "RECEIVE", (e, r, _, actor) =>
    { e.Receive(); r.MarkReceived(actor, clock.UtcNow); return Task.CompletedTask; }, token);
    public Task<ExchangeResponse> InspectAsync(Guid id, Guid itemId, ReturnInspectionRequest request, CancellationToken token) => ChangeAsync(id, false, "INSPECT", (_, r, _, _) =>
    {
        EnumText.TryParse<ReturnConditionStatus>(request.ConditionStatus, out var condition);
        r.InspectItem(itemId, condition, Texts.Clean(request.InspectionNote)); return Task.CompletedTask;
    }, token, request.InspectionNote);
    public Task<ExchangeResponse> FinishInspectionAsync(Guid id, CancellationToken token) => ChangeAsync(id, false, "COMPLETE_INSPECTION", async (e, r, o, actor) =>
    { await returns.CompleteInspectionCoreAsync(r, o, actor, token); e.FinishInspection(r.TotalRefundAmount); }, token);
    public Task<ExchangeResponse> CompleteAsync(Guid id, CompleteExchangeRequest request, CancellationToken token) => ChangeAsync(id, false, "COMPLETE", async (e, r, o, actor) =>
    {
        if (e.Status == ExchangeStatus.Completed) return;
        if (e.Status != ExchangeStatus.ReadyToComplete || request.ConfirmedDifference != e.Difference)
            throw new BusinessRuleException("Inspect the goods and confirm the exact settlement difference first.");
        if (o.FarmerProfileId is {} farmer) await locks.LockFarmerProfileAsync(farmer, token);
        var replacement = await builder.BuildAsync(Draft(o, e.Items.Select(i => new OrderItemRequest(i.StoreProductId, i.ProductPackagingId,
            i.Quantity, i.UnitPrice, "Exchange price snapshot")).ToList(), true), token);
        if (replacement.TotalAmount != e.ReplacementAmount) throw new BusinessRuleException("Replacement value changed.");
        var picks = new List<LotPick>();
        foreach (var item in e.Items)
        {
            var line = replacement.Items.Single(i => i.StoreProductId == item.StoreProductId && i.ProductPackagingId == item.ProductPackagingId);
            if (line.BaseQuantity != item.BaseQuantity) throw new BusinessRuleException("Replacement packaging conversion changed; contact the store owner.");
            var selected = request.Lots.Where(l => l.ReplacementItemId == item.Id).ToList();
            if (selected.Sum(l => l.BaseQuantity) != item.BaseQuantity) throw new BusinessRuleException("Confirm the actual lots and exact base quantity for every replacement line.");
            picks.AddRange(selected.Select(l => new LotPick(line.Id, l.InventoryLotId, l.BaseQuantity)));
        }
        if (request.Lots.Any(l => !e.Items.Any(i => i.Id == l.ReplacementItemId))) throw new BusinessRuleException("Unknown replacement item.");
        db.Orders.Add(replacement);
        var credit = Math.Min(r.TotalRefundAmount, e.ReplacementAmount);
        if (credit > 0) await AddPaymentAsync(replacement, credit, PaymentMethod.ExchangeCredit, e, actor, token);
        if (e.Difference > 0) await AddPaymentAsync(replacement, e.Difference.Value, PaymentMethod.Cash, e, actor, token);
        await confirmer.ConfirmCoreAsync(replacement, picks, token);
        await posting.PostAsync(replacement, picks.Select(p => new FulfillmentLine(p.OrderItemId, p.InventoryLotId, p.BaseQuantity)).ToList(),
            FulfillmentSource.Pickup, null, null, actor, clock.UtcNow, token);
        if (credit > 0) await AddRefundAsync(r, credit, RefundMethod.ExchangeCredit, e, actor, request.SettlementReference, token);
        if (e.Difference < 0) await AddRefundAsync(r, -e.Difference.Value, RefundMethod.Cash, e, actor, request.SettlementReference, token);
        if (r.Status != SalesReturnStatus.Completed) r.Complete(clock.UtcNow);
        e.Complete(replacement, request.ConfirmedDifference, request.SettlementReference.Trim(), clock.UtcNow);
    }, token, request.SettlementReference);

    private async Task<ExchangeResponse> ChangeAsync(Guid id, bool mine, string action,
        Func<SalesExchange, SalesReturn, Order, Guid, Task> change, CancellationToken token, string? reason = null)
    {
        var farmer = await AccessAsync(mine, action, token); var store = await ActiveStore.GetIdAsync(db, token);
        await using var tx = await db.BeginTransactionAsync(token);
        var header = await db.SalesExchanges.AsNoTracking().Where(e => e.Id == id && e.StoreId == store && (farmer == null || e.FarmerProfileId == farmer))
            .Select(e => new { e.OrderId, e.SalesReturnId }).SingleOrDefaultAsync(token) ?? throw new NotFoundException("Exchange", id);
        await locks.LockOrderAsync(header.OrderId, token); await locks.LockSalesReturnAsync(header.SalesReturnId, token);
        var exchange = await db.SalesExchanges.Include(e => e.Items).SingleAsync(e => e.Id == id && e.StoreId == store, token);
        var returned = await db.SalesReturns.Include(r => r.Items).Include(r => r.Refunds).SingleAsync(r => r.Id == header.SalesReturnId, token);
        var original = await returnQueries.OrderAsync(header.OrderId, store, farmer, false, token);
        if (action == "COMPLETE" && exchange.Status == ExchangeStatus.Completed)
        {
            await tx.CommitAsync(token); return await DetailAsync(id, farmer, token);
        }
        await change(exchange, returned, original, user.UserId!.Value);
        Record(exchange, action, reason); await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return await DetailAsync(id, farmer, token);
    }
    private async Task<ExchangeResponse> DetailAsync(Guid id, Guid? farmer, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(db, token);
        var e = await db.SalesExchanges.AsNoTracking().Include(e => e.Items).SingleOrDefaultAsync(e => e.Id == id && e.StoreId == store
            && (farmer == null || e.FarmerProfileId == farmer), token) ?? throw new NotFoundException("Exchange", id);
        var r = await returnQueries.GetAsync(e.SalesReturnId, farmer, token);
        var replacementNumber = e.ReplacementOrderId == null ? null : await db.Orders.AsNoTracking().Where(o => o.Id == e.ReplacementOrderId).Select(o => o.OrderNumber).SingleAsync(token);
        var originalItems = await db.OrderItems.IgnoreQueryFilters().AsNoTracking().Where(i => i.OrderId == e.OrderId)
            .Select(i => new ExchangeOriginalItemResponse(i.Id, i.ProductNameSnapshot, i.PackagingNameSnapshot)).ToListAsync(token);
        var history = await db.AuditLogs.AsNoTracking().Where(a => a.StoreId == store && a.EntityType == "EXCHANGE" && a.EntityId == id)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).Select(a => new ExchangeHistoryResponse(a.Action, a.OccurredAt, farmer == null ? a.ActorUserId : null, a.Reason)).ToListAsync(token);
        return new(e.Id, e.OrderId, r.OrderNumber, EnumText.Format(e.Status), e.Reason, e.ReplacementAmount, e.Difference,
            e.ReplacementOrderId, replacementNumber, e.SettlementReference, e.CreatedAt, e.CompletedAt,
            farmer == null ? r : FarmerReturnView.Redact(r), originalItems, e.Items.OrderBy(i => i.Id).Select(i => new ExchangeLineResponse(i.Id,
                i.StoreProductId, i.ProductPackagingId, i.ProductName, i.PackagingName, i.Quantity, i.BaseQuantity, i.UnitPrice, i.LineTotal)).ToList(), history);
    }
    private async Task CheckStockAsync(SalesExchange e, CancellationToken token)
    {
        var demand = e.Items.GroupBy(i => i.StoreProductId).ToList();
        var stock = await SellableStock.AvailableBaseAsync(db, demand.Select(g => g.Key).ToArray(), BusinessCalendar.Today(clock.UtcNow), token);
        if (demand.Any(g => g.Sum(i => i.BaseQuantity) > stock.GetValueOrDefault(g.Key))) throw new BusinessRuleException("Not enough sellable replacement stock.");
    }
    private async Task AddPaymentAsync(Order order, decimal amount, PaymentMethod method, SalesExchange e, Guid actor, CancellationToken token)
    {
        var number = await NumberAsync(true, e.StoreId, token);
        var p = new Payment(e.StoreId, number, PaymentContext.OrderPayment, method, amount, clock.UtcNow, e.FarmerProfileId, actor,
            "Exchange " + e.Id, orderId: order.Id);
        p.MarkPaid(PaymentConfirmationSource.Staff, clock.UtcNow, actor); db.Payments.Add(p);
        await allocator.AllocateAsync(p, order, null, actor, token);
        audit.Record("PAYMENT_RECEIVED", "PAYMENT", p.Id, e.StoreId, newValues: new { p.Amount, p.PaymentMethod, exchangeId = e.Id });
    }
    private async Task AddRefundAsync(SalesReturn r, decimal amount, RefundMethod method, SalesExchange e, Guid actor, string reference, CancellationToken token)
    {
        var refund = r.RequestRefund(await NumberAsync(false, e.StoreId, token), method, amount, actor, clock.UtcNow, note: "Exchange " + e.Id);
        r.CompleteRefund(refund.Id, actor, clock.UtcNow, reference);
        // Explicit Add makes this new child visible to subsequent number allocation without saving midway.
        db.Refunds.Add(refund);
        audit.Record("REFUND_COMPLETED", "REFUND", refund.Id, e.StoreId, newValues: new { refund.Amount, refund.RefundMethod, exchangeId = e.Id });
    }
    private async Task<string> NumberAsync(bool payment, Guid store, CancellationToken token)
    {
        var day = BusinessCalendar.Today(clock.UtcNow); var prefix = payment ? DocumentNumbers.Payment : DocumentNumbers.Refund;
        var next = payment
            ? await DocumentNumbers.NextAsync(db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.StoreId == store).Select(p => p.PaymentNumber), prefix, day, token)
            : await DocumentNumbers.NextAsync(db.Refunds.IgnoreQueryFilters().AsNoTracking().Where(r => r.StoreId == store).Select(r => r.RefundNumber), prefix, day, token);
        var local = payment ? db.Payments.Local.Where(p => p.StoreId == store).Select(p => p.PaymentNumber)
            : db.Refunds.Local.Where(r => r.StoreId == store).Select(r => r.RefundNumber);
        return DocumentNumbers.Format(prefix, day, Math.Max(DocumentNumbers.SequenceOf(next, prefix, day), local.Select(n => DocumentNumbers.SequenceOf(n, prefix, day) + 1).DefaultIfEmpty(1).Max()));
    }
    private void Record(SalesExchange e, string action, string? reason = null) => audit.Record("EXCHANGE_" + action, "EXCHANGE", e.Id, e.StoreId,
        newValues: new { e.Status, e.Difference, e.ReplacementOrderId }, reason: Texts.Clean(reason));
    private static OrderDraft Draft(Order o, IReadOnlyList<OrderItemRequest> items, bool snapshots = false) => new(
        OrderSource.Counter, o.CustomerType, SettlementType.FullPayment, FulfillmentType.Pickup, items,
        o.FarmerProfileId, o.CustomerNameSnapshot, o.CustomerPhoneSnapshot, Note: "Exchange for " + o.OrderNumber, AllowPriceOverride: snapshots);
    private static void RequireCompleted(Order o)
    { if (o.Status != OrderStatus.Completed) throw new BusinessRuleException("Only a completed order is eligible for exchange."); }
}
