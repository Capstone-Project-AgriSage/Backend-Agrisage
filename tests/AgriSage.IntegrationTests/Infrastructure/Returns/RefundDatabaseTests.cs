using System.Data.Common;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Files;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Returns;
using AgriSage.Application.Features.Deliveries;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using AgriSage.Domain.Features.Payments.Entities;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgriSage.IntegrationTests.Infrastructure.Returns;

public class RefundDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : IDateTimeProvider { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Storage : IFileStorageService
    {
        public bool Deleted { get; private set; }
        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string key, StorageArea area, CancellationToken token) { Deleted = true; return Task.CompletedTask; }
        public string? KeyFromPublicUrl(string url, StorageArea area) => null;
    }
    private sealed class FailRefundWrite : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // Fail at the database AFTER the mutation statements, inside EF's SaveChanges savepoint.
            if (command.CommandText.Contains("UPDATE refunds", StringComparison.OrdinalIgnoreCase)) command.CommandText += "\nSELECT 1 / 0;";
            return ValueTask.FromResult(result);
        }
    }
    private sealed record Env(RealDb.Session Session, Guid Staff, Guid FarmerUser, Guid Farmer, Guid Order, Guid Payment,
        Guid Return, Guid Lot, Guid? CancellationRefund)
    {
        public async Task<T> Run<T>(Func<RefundService, Task<T>> call, bool fail = false)
        {
            Session.CurrentUser.UserId = Staff; Session.CurrentUser.Role = "STORE_OWNER";
            await using var db = fail ? Session.NewContext(new FailRefundWrite()) : Session.NewContext(); var clock = new Clock();
            return await call(new RefundService(db, new RowLockService(db), Session.CurrentUser, clock, new AuditTrail(db, Session.CurrentUser, clock)));
        }
        public Task<RefundResponse> Request(decimal amount, Guid? payment = null) => Run(s => s.CreateReturnAsync(Return, new("CASH", amount, payment), Token));
        public Task<RefundResponse> Complete(Guid id, CompleteRefundRequest? request = null, bool fail = false) => Run(s => s.CompleteReturnAsync(Return, id, request ?? new(), Token), fail);
    }
    private static async Task<Env> Prepare(RealDb.Session session, int fulfilled = 10, bool cancel = false, decimal paidAmount = 1000m)
    {
        await using var db = session.NewContext(); var tag = Guid.NewGuid().ToString("N")[..10]; var clock = new Clock(); var now = clock.UtcNow;
        async Task<User> Actor(RoleCode code)
        {
            var role = await db.Roles.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.Code == code, Token);
            if (role is null) { role = new Role(code, code.ToString()); db.Roles.Add(role); }
            var user = new User(role.Id, "Refund tester", "hash", $"{tag}-{code}@example.test", null); db.Users.Add(user); return user;
        }
        var staff = await Actor(RoleCode.StoreOwner); var user = await Actor(RoleCode.Farmer); var farmer = new FarmerProfile(user.Id); db.FarmerProfiles.Add(farmer);
        session.CurrentUser.UserId = staff.Id; session.CurrentUser.Role = "STORE_OWNER";
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null) { store = new Store($"S-{tag}", "Test", "Address", "Province"); db.Stores.Add(store); }
        var category = new Category($"C-{tag}", "Test"); var unit = new Unit($"U-{tag}", "Test"); var product = new Product(category.Id, $"P-{tag}", "Refund product");
        var packaging = product.AddPackaging(unit.Id, 1, true, true, true, "ACTIVE"); var sp = new StoreProduct(store.Id, product.Id);
        var lot = new InventoryLot(sp.Id, "ORIGINAL"); lot.ReceiveStock(30, 10m);
        var order = new Order(store.Id, $"OD-{tag}", OrderSource.Counter, CustomerType.Registered, staff.Id, "Tester",
            SettlementType.FullPayment, FulfillmentType.Pickup, farmer.Id);
        var item = order.AddItem(sp, packaging, product.Sku, product.Name, "Base", 10, 100m); order.Confirm(staff.Id, now);
        var payment = new Payment(store.Id, $"PM-{tag}", PaymentContext.OrderPayment, PaymentMethod.Cash, paidAmount, now, farmer.Id, staff.Id, orderId: order.Id);
        payment.MarkPaid(PaymentConfirmationSource.Staff, now, staff.Id); var allocation = payment.AllocateToOrder(order.Id, paidAmount, now, staff.Id);
        SalesReturn? salesReturn = null;
        if (fulfilled > 0)
        {
            var sale = new StockMovement(store.Id, $"SM-{tag}", StockMovementType.Sale, now, staff.Id, orderId: order.Id);
            var saleItem = sale.AddItem(lot.Id, lot.IssueUnreserved(fulfilled)); sale.Post(staff.Id, now); db.StockMovements.Add(sale);
            order.RecordFulfillment(item.Id, fulfilled, staff.Id, now); payment.ConsumePrepayment(allocation.Id, fulfilled * 100m);
            salesReturn = new SalesReturn(order, $"RT-{tag}", staff.Id, now);
            var line = salesReturn.AddItem(order, item, fulfilled, 0, lot.Id, "OTHER", originalStockMovementItemId: saleItem.Id, originalCogsUnitCost: 10m);
            salesReturn.Approve(staff.Id, now); salesReturn.MarkReceived(staff.Id, now); salesReturn.InspectItem(line.Id, ReturnConditionStatus.Damaged);
            salesReturn.CompleteInspection(staff.Id, now, 0); salesReturn.FinishInspectionResolution(now); db.SalesReturns.Add(salesReturn);
        }
        db.AddRange(category, unit, product, sp, lot, order, payment); await db.SaveChangesAsync(Token);
        Guid? cancellationRefund = null;
        if (cancel)
        {
            if (fulfilled == 0) order.Cancel(staff.Id, now, "Test cancellation"); else order.CancelItemRemaining(item.Id, staff.Id, now, "Test partial cancellation");
            var refunds = await new OrderPaymentCancellation(db, new RowLockService(db), null!, clock, new AuditTrail(db, session.CurrentUser, clock))
                .ReverseForCancelledOrderAsync(order, staff.Id, "Test cancellation", Token);
            cancellationRefund = Assert.Single(refunds).RefundId;
            await db.SaveChangesAsync(Token);
        }
        return new(session, staff.Id, user.Id, farmer.Id, order.Id, payment.Id, salesReturn?.Id ?? Guid.Empty, lot.Id, cancellationRefund);
    }

    [RealDbFact]
    public async Task Return_refunds_reserve_capacity_complete_payment_in_parts_and_finish_return_on_last_refund()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session);
        var first = await env.Run(s => s.CreateReturnAsync(env.Return, new("BANK_TRANSFER", 400m, env.Payment, "requested-reference", "request note"), Token));
        Assert.Equal("PENDING", first.Status); Assert.Equal("SALES_RETURN", first.Source); Assert.Equal("requested-reference", first.ExternalReference);
        var second = await env.Request(600m, env.Payment); Assert.NotEqual(first.RefundNumber, second.RefundNumber);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Request(1m, env.Payment));
        var completed = await env.Complete(first.Id, new(Note: "paid externally")); Assert.Equal("COMPLETED", completed.Status);
        Assert.Equal("requested-reference", completed.ExternalReference); Assert.Equal("paid externally", completed.Note);
        await using var db = session.NewContext();
        Assert.Equal(PaymentStatus.PartiallyRefunded, await db.Payments.Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        Assert.Equal(SalesReturnStatus.PartiallyResolved, await db.SalesReturns.Where(r => r.Id == env.Return).Select(r => r.Status).SingleAsync(Token));
        var summary = await new PaymentQueries(db).GetOrderSummaryAsync(env.Order, env.Farmer, Token);
        Assert.Equal(1000m, summary.PaidAmount);
        Assert.Equal(1000m, await new OrderPrepaymentLedger(db).GetPaidAmountAsync(env.Order, Token));
        await env.Complete(second.Id);
        Assert.Equal(PaymentStatus.Refunded, await db.Payments.AsNoTracking().Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        Assert.Equal(SalesReturnStatus.Completed, await db.SalesReturns.AsNoTracking().Where(r => r.Id == env.Return).Select(r => r.Status).SingleAsync(Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Complete(second.Id));
        Assert.Equal(20, await db.InventoryLotBalances.Where(b => b.InventoryLotId == env.Lot).Select(b => b.QuantityOnHand).SingleAsync(Token));
    }
    [RealDbFact]
    public async Task Failed_and_cancelled_return_refunds_release_capacity_and_retry_is_a_new_record()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session);
        var failed = await env.Request(1000m); await env.Run(s => s.FailReturnAsync(env.Return, failed.Id, new("bank transfer failed"), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Complete(failed.Id));
        var cancelled = await env.Request(1000m); var response = await env.Run(s => s.CancelReturnAsync(env.Return, cancelled.Id, new("wrong method"), Token));
        Assert.Equal("wrong method", response.CancelReason); Assert.Equal(env.Staff, response.CancelledBy);
        var retry = await env.Request(1000m); Assert.NotEqual(failed.Id, retry.Id); Assert.NotEqual(cancelled.RefundNumber, retry.RefundNumber);
        await env.Complete(retry.Id); await using var db = session.NewContext();
        Assert.Equal(PaymentStatus.Paid, await db.Payments.Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        Assert.Equal(SalesReturnStatus.Completed, await db.SalesReturns.Where(r => r.Id == env.Return).Select(r => r.Status).SingleAsync(Token));
    }
    [RealDbFact]
    public async Task Cancelled_order_refunds_created_by_F1_6_are_completed_failed_cancelled_and_retried_with_fixed_cap()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session, fulfilled: 0, cancel: true);
        var original = Assert.Single(await env.Run(s => s.ListOrderAsync(env.Order, Token))); Assert.Equal(env.CancellationRefund, original.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 1), Token)));
        await env.Run(s => s.FailOrderAsync(env.Order, original.Id, new("failed transfer"), Token));
        var retry = await env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 1000, "retry"), Token));
        await env.Run(s => s.CancelOrderAsync(env.Order, retry.Id, new("wrong recipient"), Token));
        var part = await env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 600), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 401), Token)));
        var rest = await env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 400), Token));
        await env.Run(s => s.CompleteOrderAsync(env.Order, part.Id, new("cash-600", Note: "paid"), Token));
        await env.Run(s => s.CompleteOrderAsync(env.Order, rest.Id, new(), Token));
        await Assert.ThrowsAsync<DomainException>(() => env.Run(s => s.FailOrderAsync(env.Order, rest.Id, new(), Token)));
        await using var db = session.NewContext(); Assert.Equal(PaymentStatus.Refunded, await db.Payments.Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        var my = new MyPaymentService(new PaymentQueries(db), new RealDb.MutableUser { UserId = env.FarmerUser, Role = "FARMER" });
        Assert.Equal(4, (await my.GetOrderSummaryAsync(env.Order, Token)).Refunds.Count);
    }
    [RealDbFact]
    public async Task Both_sources_share_payment_capacity_and_partial_cancellation_cannot_refund_consumed_money()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session, fulfilled: 4, cancel: true);
        var cancellation = Assert.Single(await env.Run(s => s.ListOrderAsync(env.Order, Token))); Assert.Equal(600m, cancellation.Amount);
        var returned = await env.Request(400m, env.Payment);
        await env.Run(s => s.FailOrderAsync(env.Order, cancellation.Id, new(), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 601), Token)));
        var retry = await env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 600), Token));
        await env.Complete(returned.Id); await env.Run(s => s.CompleteOrderAsync(env.Order, retry.Id, new(), Token));
        await using var db = session.NewContext(); Assert.Equal(PaymentStatus.Refunded, await db.Payments.Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        var allocation = await db.PaymentAllocations.AsNoTracking().SingleAsync(a => a.PaymentId == env.Payment, Token);
        Assert.Equal(400m, allocation.AllocatedAmount); Assert.Equal(400m, allocation.PrepaymentConsumedAmount); Assert.Equal(PaymentAllocationStatus.Active, allocation.Status);
    }
    [RealDbFact]
    public async Task Return_then_cancellation_still_releases_unconsumed_prepayment_of_partially_refunded_payment()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session, fulfilled: 4);
        var returned = await env.Request(400, env.Payment); await env.Complete(returned.Id);
        await using var db = session.NewContext(); var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == env.Order, Token);
        order.CancelItemRemaining(Assert.Single(order.Items).Id, env.Staff, DateTimeOffset.UtcNow, "Cancel rest");
        var clock = new Clock(); var refunds = await new OrderPaymentCancellation(db, new RowLockService(db), null!, clock, new AuditTrail(db, session.CurrentUser, clock))
            .ReverseForCancelledOrderAsync(order, env.Staff, "Cancel rest", Token);
        Assert.Equal(600m, Assert.Single(refunds).Amount); await db.SaveChangesAsync(Token);
        await env.Run(s => s.CompleteOrderAsync(env.Order, refunds[0].RefundId, new(), Token));
        Assert.Equal(PaymentStatus.Refunded, await db.Payments.AsNoTracking().Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
    }
    [RealDbFact]
    public async Task Cancellation_cannot_create_refunds_above_a_payment_cap_already_reserved_by_a_return()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await Prepare(session, fulfilled: 4, paidAmount: 600m);
        Guid secondPaymentId;
        await using (var db = session.NewContext())
        {
            var original = await db.Payments.SingleAsync(p => p.Id == env.Payment, Token);
            var now = DateTimeOffset.UtcNow;
            var second = new Payment(original.StoreId, $"PM-{Guid.NewGuid():N}", PaymentContext.OrderPayment,
                PaymentMethod.Cash, 400m, now, env.Farmer, env.Staff, orderId: env.Order);
            second.MarkPaid(PaymentConfirmationSource.Staff, now, env.Staff);
            second.AllocateToOrder(env.Order, 400m, now, env.Staff);
            db.Payments.Add(second); await db.SaveChangesAsync(Token); secondPaymentId = second.Id;
        }
        var returned = await env.Request(400m, secondPaymentId);
        async Task<IReadOnlyList<CancellationRefundInfo>> CancelRemainder()
        {
            await using var db = session.NewContext();
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == env.Order, Token);
            order.CancelItemRemaining(Assert.Single(order.Items).Id, env.Staff, DateTimeOffset.UtcNow, "Cancel rest");
            var clock = new Clock();
            var result = await new OrderPaymentCancellation(db, new RowLockService(db), null!, clock,
                new AuditTrail(db, session.CurrentUser, clock)).ReverseForCancelledOrderAsync(order, env.Staff, "Cancel rest", Token);
            await db.SaveChangesAsync(Token); return result;
        }
        await Assert.ThrowsAsync<BusinessRuleException>(CancelRemainder);
        await env.Complete(returned.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(CancelRemainder);
        await using var verify = session.NewContext();
        Assert.Empty(await verify.Refunds.Where(r => r.OrderId == env.Order).ToListAsync(Token));
        Assert.Equal(1000m, await verify.PaymentAllocations.Where(a => a.OrderId == env.Order).SumAsync(a => a.AllocatedAmount, Token));
        Assert.NotEqual(OrderStatus.PartiallyCancelled, await verify.Orders.Where(o => o.Id == env.Order).Select(o => o.Status).SingleAsync(Token));
    }

    [RealDbFact]
    public async Task Wrong_parent_unpaid_payment_and_uncancelled_order_are_rejected_without_changes()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var other = await Prepare(session);
        var refund = await env.Request(100, env.Payment);
        await Assert.ThrowsAsync<NotFoundException>(() => other.Run(s => s.CompleteReturnAsync(other.Return, refund.Id, new(), Token)));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Request(100, other.Payment));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Run(s => s.CreateOrderAsync(env.Order, new(env.Payment, "CASH", 100), Token)));
        await using var db = session.NewContext(); var paid = await db.Payments.SingleAsync(p => p.Id == env.Payment, Token);
        var pending = new Payment(paid.StoreId, $"PM-pending-{Guid.NewGuid():N}", PaymentContext.OrderPayment, PaymentMethod.Cash, 100m,
            DateTimeOffset.UtcNow, env.Farmer, env.Staff, orderId: env.Order); db.Payments.Add(pending); await db.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Request(100, pending.Id));
        Assert.Equal(1, await db.Refunds.CountAsync(r => r.SalesReturnId == env.Return, Token));
    }
    [RealDbFact]
    public async Task Database_failure_rolls_back_refund_payment_return_completion_and_audit_together()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var refund = await env.Request(1000, env.Payment);
        await Assert.ThrowsAsync<DbUpdateException>(() => env.Complete(refund.Id, fail: true));
        await using var db = session.NewContext();
        Assert.Equal(RefundStatus.Pending, await db.Refunds.Where(r => r.Id == refund.Id).Select(r => r.Status).SingleAsync(Token));
        Assert.Equal(PaymentStatus.Paid, await db.Payments.Where(p => p.Id == env.Payment).Select(p => p.Status).SingleAsync(Token));
        Assert.Equal(SalesReturnStatus.PartiallyResolved, await db.SalesReturns.Where(r => r.Id == env.Return).Select(r => r.Status).SingleAsync(Token));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == refund.Id && a.Action == "REFUND_COMPLETED", Token));
        await env.Complete(refund.Id);
    }
    [RealDbFact]
    public async Task Refund_proof_url_blocks_photo_deletion_without_calling_storage_and_unused_keys_remain_deletable()
    {
        await using var session = await RealDb.Session.StartAsync(); var env = await Prepare(session); var refund = await env.Request(1000);
        var key = $"2026/10/{Guid.NewGuid():N}.jpg";
        await env.Complete(refund.Id, new(ProofFileUrl: $"https://example.test/delivery-proofs/{key}?download=1"));
        await using var db = session.NewContext(); var storage = new Storage(); var proofs = new DeliveryProofService(storage, new Clock(), new DeliveryProofUsage(db));
        await Assert.ThrowsAsync<BusinessRuleException>(() => proofs.DeleteAsync(key, Token)); Assert.False(storage.Deleted);
        await proofs.DeleteAsync($"2026/10/{Guid.NewGuid():N}.jpg", Token); Assert.True(storage.Deleted);
    }
}
