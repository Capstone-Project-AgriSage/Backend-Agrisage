using System.Text.Json;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Orders;
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
using AgriSage.IntegrationTests.Infrastructure.Authentication;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Notifications;

public sealed class NotificationCoverageDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Tag => Guid.NewGuid().ToString("N");

    private static async Task<(User User, StoreMember Member)> MemberAsync(OperationsTestEnvironment e, RoleCode code,
        Guid? storeId = null, bool review = false)
    {
        var role = await e.Context.Roles.SingleAsync(r => r.Code == code, Token);
        var user = new User(role.Id, "Coverage staff", "hash", $"{Tag}@example.test", null);
        var member = new StoreMember(storeId ?? e.Store.Id, user.Id);
        if (review) member.GrantAiReview();
        e.Context.AddRange(user, member);
        return (user, member);
    }

    [RealDbFact]
    public async Task Business_notifications_match_the_role_matrix_and_never_reach_other_customers_or_stores()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var owner = await MemberAsync(e, RoleCode.StoreOwner);
        var sales = await MemberAsync(e, RoleCode.SalesStaff);
        await MemberAsync(e, RoleCode.DeliveryStaff);
        var otherStore = new Store("S" + Tag[..12], "Other", "Address", "Province", status: StoreStatus.Inactive);
        e.Context.Stores.Add(otherStore);
        await MemberAsync(e, RoleCode.StoreOwner, otherStore.Id);
        await MemberAsync(e, RoleCode.SalesStaff, otherStore.Id);
        var otherFarmer = new User(e.User.RoleId, "Other farmer", "hash", $"{Tag}@example.test", null);
        e.Context.AddRange(otherFarmer, new AgriSage.Domain.Features.Customers.Entities.FarmerProfile(otherFarmer.Id));
        var address = new DeliveryAddress("Farmer", "0901234567", "Address", "Province");
        var order = new Order(e.Store.Id, "OD-" + Tag, OrderSource.FarmerWeb, CustomerType.Registered, e.User.Id,
            "Farmer", SettlementType.FullPayment, FulfillmentType.Delivery, e.Farmer.Id, deliveryAddress: address);
        var delivery = new Delivery(order, "DL-" + Tag, address, owner.User.Id);
        var payment = new Payment(e.Store.Id, "PM-" + Tag, PaymentContext.OrderPayment, PaymentMethod.Cash,
            100m, e.Time.UtcNow, e.Farmer.Id, owner.User.Id, orderId: order.Id);
        var repayment = new Payment(e.Store.Id, "PM-" + Tag, PaymentContext.DebtRepayment, PaymentMethod.Cash,
            100m, e.Time.UtcNow, e.Farmer.Id, owner.User.Id);
        payment.MarkPaid(PaymentConfirmationSource.Staff, e.Time.UtcNow, owner.User.Id);
        var salesReturn = new SalesReturn(order, "RT-" + Tag, e.User.Id, e.Time.UtcNow);
        order.Cancel(owner.User.Id, e.Time.UtcNow);
        var refund = order.RequestCancellationRefund("RF-" + Tag, payment.Id, RefundMethod.Cash, 10m, owner.User.Id, e.Time.UtcNow);
        var credit = new FarmerCreditProfile(e.Store.Id, e.Farmer.Id, 1000m, owner.User.Id, e.Time.UtcNow);
        var account = new DebtAccount(e.Store.Id, e.Farmer.Id);
        var debt = account.CreateManualAdjustmentEntry("DE-" + Tag, 100m, DateOnly.FromDateTime(e.Time.UtcNow.UtcDateTime), owner.User.Id, e.Time.UtcNow);
        e.Context.AddRange(order, delivery, payment, repayment, salesReturn, credit, account, debt.Entry, debt.Transaction);
        await e.Context.SaveChangesAsync(Token);
        var staff = new[] { owner.User.Id, sales.User.Id };
        var all = new[] { owner.User.Id, sales.User.Id, e.User.Id };
        var saleFarmer = new[] { sales.User.Id, e.User.Id };
        var farmer = new[] { e.User.Id };
        var rows = new (string Action, string Entity, Guid Id, Guid[] Recipients)[]
        {
            ("ORDER_PLACED", "ORDER", order.Id, staff),
            ("ORDER_CREATED", "ORDER", order.Id, staff),
            ("COUNTER_SALE_COMPLETED", "ORDER", order.Id, all),
            ("ORDER_CANCELLED", "ORDER", order.Id, all),
            ("ORDER_ITEM_REMAINING_CANCELLED", "ORDER", order.Id, all),
            ("ORDER_CONFIRMED", "ORDER", order.Id, saleFarmer),
            ("ORDER_PREPARING_STARTED", "ORDER", order.Id, saleFarmer),
            ("ORDER_MARKED_READY", "ORDER", order.Id, saleFarmer),
            ("ORDER_PICKED_UP", "ORDER", order.Id, saleFarmer),
            ("DELIVERY_CREATED", "DELIVERY", delivery.Id, [sales.User.Id]),
            ("DELIVERY_DISPATCHED", "DELIVERY", delivery.Id, saleFarmer),
            ("DELIVERY_ATTEMPT_COMPLETED", "DELIVERY", delivery.Id, saleFarmer),
            ("DELIVERY_CANCELLED", "DELIVERY", delivery.Id, saleFarmer),
            ("PAYMENT_RECEIVED", "PAYMENT", payment.Id, saleFarmer),
            ("PAYMENT_FAILED", "PAYMENT", payment.Id, saleFarmer),
            ("PAYMENT_RECEIVED", "PAYMENT", repayment.Id, all),
            ("DEBT_PAYMENT_CONFIRMED", "PAYMENT", repayment.Id, all),
            ("DEBT_PAYMENT_REJECTED", "PAYMENT", repayment.Id, saleFarmer),
            ("DEBT_CREATED", "DEBT_ENTRY", debt.Entry.Id, farmer),
            ("DEBT_MANUAL_ENTRY", "DEBT_ENTRY", debt.Entry.Id, farmer),
            ("DEBT_DISPUTE", "DEBT_ENTRY", debt.Entry.Id, staff),
            ("CUSTOMER_CREDIT_LIMIT_CHANGED", "CREDIT_PROFILE", credit.Id, [owner.User.Id, e.User.Id]),
            ("RETURN_REQUESTED", "SALES_RETURN", salesReturn.Id, staff),
            ("RETURN_APPROVED", "SALES_RETURN", salesReturn.Id, farmer),
            ("RETURN_REJECTED", "SALES_RETURN", salesReturn.Id, farmer),
            ("RETURN_CANCELLED", "SALES_RETURN", salesReturn.Id, farmer),
            ("RETURN_INSPECTION_COMPLETED", "SALES_RETURN", salesReturn.Id, farmer),
            ("REFUND_PENDING", "REFUND", refund.Id, staff),
            ("REFUND_COMPLETED", "REFUND", refund.Id, farmer),
            ("REFUND_FAILED", "REFUND", refund.Id, farmer),
            ("REFUND_CANCELLED", "REFUND", refund.Id, farmer)
        };
        var dispatcher = new NotificationDispatcher(e.Context, new NotificationOutboxLock(e.Context), new NotificationWriter(e.Context), e.Time);
        foreach (var row in rows)
        {
            var log = e.Audit.Record(row.Action, row.Entity, row.Id, e.Store.Id, newValues: new { status = "FAILED" });
            await e.Context.SaveChangesAsync(Token);
            var item = await e.Context.NotificationOutbox.SingleAsync(o => o.AuditLogId == log.Id, Token);
            await dispatcher.DispatchAsync(item.Id, Token);
            await dispatcher.DispatchAsync(item.Id, Token);
            var notices = await e.Context.Notifications.Where(n => n.DeduplicationKey == $"audit:{log.Id:N}").ToListAsync(Token);
            Assert.Equal(row.Recipients.Order(), notices.Select(n => n.UserId).Order());
            if (row.Entity is "ORDER" or "DELIVERY" or "SALES_RETURN" or "REFUND" || row.Id == payment.Id)
                Assert.All(notices, n => Assert.Equal(order.Id, JsonDocument.Parse(n.Data!).RootElement.GetProperty("orderId").GetGuid()));
            if (row.Action == "DELIVERY_ATTEMPT_COMPLETED") Assert.All(notices, n => Assert.Equal("DELIVERY_FAILED", n.NotificationType));
            if (row.Action == "PAYMENT_RECEIVED" && row.Id == repayment.Id) Assert.All(notices, n => Assert.Equal("DEBT_PAYMENT_CONFIRMED", n.NotificationType));
            if (row.Action == "DEBT_DISPUTE") Assert.All(notices, n => Assert.Equal("DEBT_DISPUTED", n.NotificationType));
        }
        var mismatch = e.Audit.Record("ORDER_CANCELLED", "ORDER", order.Id, otherStore.Id);
        await e.Context.SaveChangesAsync(Token);
        await dispatcher.DispatchAsync((await e.Context.NotificationOutbox.SingleAsync(o => o.AuditLogId == mismatch.Id, Token)).Id, Token);
        Assert.False(await e.Context.Notifications.AnyAsync(n => n.DeduplicationKey == $"audit:{mismatch.Id:N}", Token));
    }

    [RealDbFact]
    public async Task Debt_reminders_reach_staff_even_if_the_farmer_was_already_notified_and_do_not_change_the_ledger()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var owner = await MemberAsync(e, RoleCode.StoreOwner);
        var sales = await MemberAsync(e, RoleCode.SalesStaff);
        var account = new DebtAccount(e.Store.Id, e.Farmer.Id);
        var today = AgriSage.Application.Common.BusinessCalendar.Today(e.Time.UtcNow);
        var overdue = account.CreateManualAdjustmentEntry("DE-" + Tag, 100m, today.AddDays(-1), owner.User.Id, e.Time.UtcNow);
        var soon = account.CreateManualAdjustmentEntry("DE-" + Tag, 200m, today.AddDays(2), owner.User.Id, e.Time.UtcNow);
        e.Context.AddRange(account, overdue.Entry, overdue.Transaction, soon.Entry, soon.Transaction);
        var writer = new NotificationWriter(e.Context);
        await e.Context.SaveChangesAsync(Token);
        await writer.AddAsync([e.User.Id], "DEBT_OVERDUE", "Old reminder", "Message",
            $"debt:{overdue.Entry.Id}:{today:yyyyMMdd}", "DEBT_ENTRY", overdue.Entry.Id, Token);
        await e.Context.SaveChangesAsync(Token);
        var service = new OperationalAlertsService(e.Context, writer, new AgriSage.Application.Features.Inventory.InventoryService(
            e.Context, e.Time, new RowLockService(e.Context)), e.Time);
        await service.DebtRemindersAsync(1, 3, Token);
        await service.DebtRemindersAsync(1, 3, Token);
        await service.DebtRemindersAsync(1, 3, Token);
        var notices = await e.Context.Notifications.Where(n => n.NotificationType == "DEBT_OVERDUE" || n.NotificationType == "DEBT_DUE_SOON").ToListAsync(Token);
        Assert.Equal(6, notices.Count);
        Assert.Equal(2, notices.Count(n => n.UserId == owner.User.Id));
        Assert.Equal(2, notices.Count(n => n.UserId == sales.User.Id));
        Assert.Equal(2, notices.Count(n => n.UserId == e.User.Id));
        Assert.Equal(300m, account.CurrentBalance);
        Assert.Equal(2, await e.Context.DebtTransactions.CountAsync(t => t.DebtAccountId == account.Id, Token));
    }

    [RealDbFact]
    public async Task Committed_stock_notifies_only_owners_with_bounded_progress_and_deduplication()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var owner = await MemberAsync(e, RoleCode.StoreOwner);
        await MemberAsync(e, RoleCode.SalesStaff);
        var category = new Category("C" + Tag[..12], "Test");
        var product = new Product(category.Id, "P" + Tag[..12], "Test");
        var sp = new StoreProduct(e.Store.Id, product.Id);
        var lot = new InventoryLot(sp.Id, "LOT-" + Tag);
        e.Context.AddRange(category, product, sp, lot);
        var received = new StockMovement(e.Store.Id, "SM-" + Tag, StockMovementType.StockIn, e.Time.UtcNow, owner.User.Id);
        received.AddItem(lot.Id, lot.ReceiveStock(10, 1m)); received.Post(owner.User.Id, e.Time.UtcNow);
        var issued = new StockMovement(e.Store.Id, "SM-" + Tag, StockMovementType.Sale, e.Time.UtcNow, owner.User.Id);
        issued.AddItem(lot.Id, lot.IssueUnreserved(2)); issued.Post(owner.User.Id, e.Time.UtcNow);
        var adjusted = new StockMovement(e.Store.Id, "SM-" + Tag, StockMovementType.AdjustmentOut, e.Time.UtcNow, owner.User.Id);
        adjusted.AddItem(lot.Id, lot.IssueUnreserved(1)); adjusted.Post(owner.User.Id, e.Time.UtcNow);
        var draft = new StockMovement(e.Store.Id, "SM-" + Tag, StockMovementType.AdjustmentOut, e.Time.UtcNow, owner.User.Id);
        e.Context.AddRange(received, issued, adjusted, draft);
        await e.Context.SaveChangesAsync(Token);
        var collector = new CommittedNotificationService(e.Context, new NotificationWriter(e.Context));
        for (var i = 0; i < 4; i++) await collector.StockAsync(1, Token);
        var keys = new[] { $"stock-posted:{received.Id}", $"stock-posted:{issued.Id}", $"stock-posted:{adjusted.Id}" };
        var notices = await e.Context.Notifications.Where(n => keys.Contains(n.DeduplicationKey!)).ToListAsync(Token);
        Assert.Equal(3, notices.Count);
        Assert.All(notices, n => Assert.Equal(owner.User.Id, n.UserId));
        Assert.Equal(new[] { "STOCK_ADJUSTED", "STOCK_ISSUED", "STOCK_RECEIVED" }, notices.Select(n => n.NotificationType).Order());
        Assert.False(await e.Context.Notifications.AnyAsync(n => n.DeduplicationKey == $"stock-posted:{draft.Id}", Token));
        Assert.Equal(7, lot.Balance.QuantityOnHand);
        Assert.Equal(4, await e.Context.StockMovements.CountAsync(m => m.StoreId == e.Store.Id, Token));
    }

    [RealDbFact]
    public async Task Diagnosis_alerts_use_owned_persisted_results_and_require_authorized_human_approval()
    {
        await using var e = await OperationsTestEnvironment.CreateAsync();
        var reviewer = await MemberAsync(e, RoleCode.SalesStaff);
        var disease = await e.Context.Diseases.SingleAsync(d => d.Code == "LEAF_BLAST", Token);
        var model = new AiModel("test-" + Tag, "1", "PyTorch", "https://example.test/model", "[]", reviewer.User.Id);
        model.Activate(e.Time.UtcNow.AddDays(-1));
        var policy = new AiPolicyConfig(model.Id, "1", .7m, e.Time.UtcNow.AddDays(-1), reviewer.User.Id); policy.Activate();
        var c = new DiagnosisCase(e.Farmer.Id, "DC-" + Tag, e.Time.UtcNow);
        var image = c.AddImage("cases/test.jpg", "https://example.test/test.jpg", e.Time.UtcNow, isPrimary: true);
        c.StartProcessing();
        var inference = c.RecordInference(image.Id, model, policy, disease.Code, .9m, true, AiInferenceStatus.Success, e.Time.UtcNow, disease.Id);
        c.CompleteAi();
        e.Context.AddRange(model, policy, c);
        await e.Context.SaveChangesAsync(Token);
        var collector = new CommittedNotificationService(e.Context, new NotificationWriter(e.Context));
        await collector.DiagnosisAsync(1, Token);
        Assert.Equal("AI_DIAGNOSIS_COMPLETED", (await e.Context.Notifications.SingleAsync(n => n.UserId == e.User.Id, Token)).NotificationType);
        var review = c.Review(reviewer.Member.Id, AgentReviewDecision.Confirmed, e.Time.UtcNow, disease, inference.Id);
        var treatment = new DiseaseTreatment(disease.Id, TreatmentType.Cultural, "Guidance", "Approved guidance");
        e.Context.DiseaseTreatments.Add(treatment);
        c.AddRecommendation(RecommendationType.Treatment, disease, reviewer.User.Id, e.Time.UtcNow, diseaseTreatment: treatment);
        var category = new Category("C" + Tag[..12], "Test");
        var product = new Product(category.Id, "P" + Tag[..12], "Approved product");
        var storeProduct = new StoreProduct(e.Store.Id, product.Id);
        e.Context.AddRange(category, product, storeProduct);
        c.AddRecommendation(RecommendationType.Product, disease, reviewer.User.Id, e.Time.UtcNow, storeProduct: storeProduct);
        await e.Context.SaveChangesAsync(Token);
        await collector.DiagnosisAsync(1, Token);
        Assert.Equal(1, await e.Context.Notifications.CountAsync(n => n.UserId == e.User.Id, Token)); // no can_review_ai permission yet
        reviewer.Member.GrantAiReview();
        await e.Context.SaveChangesAsync(Token);
        await collector.DiagnosisAsync(1, Token);
        await collector.DiagnosisAsync(1, Token);
        var notices = await e.Context.Notifications.Where(n => n.UserId == e.User.Id).ToListAsync(Token);
        Assert.Equal(3, notices.Count);
        Assert.Contains(notices, n => n.NotificationType == "DIAGNOSIS_REVIEWED");
        Assert.Contains(notices, n => n.NotificationType == "DIAGNOSIS_RECOMMENDATIONS"
            && n.Title == "Đã có khuyến nghị được chuyên gia phê duyệt");
        Assert.All(notices, n => Assert.Equal(c.Id, JsonDocument.Parse(n.Data!).RootElement.GetProperty("entityId").GetGuid()));
        Assert.False(await e.Context.Notifications.AnyAsync(n => n.UserId == reviewer.User.Id, Token));
        Assert.Equal(DiagnosisCaseStatus.Verified, c.Status);
        Assert.True(review.IsCurrent);
    }
}
