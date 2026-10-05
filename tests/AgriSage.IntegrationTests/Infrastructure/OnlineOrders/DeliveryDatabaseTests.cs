using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Placeholders;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Deliveries;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// FLOW_2 §7–9 (tasks F2.5–F2.7) against PostgreSQL; every session rolls back. Covers the "must prove" list of §10:
// planned quantity limit, lot change history + reservation move, delivery-staff scope, 20 → 18 + 2, proof required,
// one attempt in progress, a failed attempt posts nothing, two trips complete the order, Farmer tracking and the report.
[Collection(RealDb.WalkInPriceListCollection)]
public class DeliveryDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private const string Proof = "https://cdn.example.com/2026/10/0123456789abcdef0123456789abcdef.jpg";

    // Only "https://cdn.example.com/<key>" is this store's delivery photo.
    private sealed class FakeStorage : IFileStorageService
    {
        public Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken) => Task.CompletedTask;

        public string? KeyFromPublicUrl(string url, StorageArea area) =>
            area == StorageArea.DeliveryProofs && url.StartsWith("https://cdn.example.com/", StringComparison.Ordinal)
                ? url["https://cdn.example.com/".Length..]
                : null;
    }

    private sealed class Env(OnlineOrderTestData data)
    {
        private readonly NpgsqlErrorClassifier _errors = new();
        private readonly DateTimeProvider _clock = new();

        public OnlineOrderTestData Data { get; } = data;

        private AgriSageDbContext Context => Data.Context;

        private AuditTrail Audit => new(Context, Data.User, _clock);

        private RowLockService Locks => new(Context);

        private OrderQueries OrderQueries => new(Context);

        private DeliveryAccess Access => new(Context, Data.User);

        private DeliveryQueries Queries => new(Context);

        public OrderService Orders => new(Context, new OrderBuilder(Context, new PriceResolver(Context), Data.User, _clock, Audit),
            OrderQueries, Data.User, _clock, _errors, Audit);

        public OrderConfirmationService Confirmation => new(Context, Locks,
            new OrderConfirmer(Context, Locks, new TemporaryOrderSettlementGuard(), Data.User, _clock, Audit), OrderQueries, _clock,
            _errors, Audit);

        public DeliveryService Deliveries => new(Context, Access, Queries, Locks, _clock, _errors, Audit);

        public DeliveryAttemptService Attempts => new(Context, Access, Queries, new DeliveryProofUrls(new FakeStorage()),
            new FulfillmentPostingService(Context, Locks, new TemporaryFulfillmentFinancialPosting(new OrderPrepaymentLedger(Context))),
            Locks, _clock, Audit);

        public DeliveryIncidentService Incidents => new(Context, Access, new DeliveryProofUrls(new FakeStorage()), Locks, _clock, Audit);

        public MyDeliveryService Mine => new(Context, new CurrentFarmer(Context, Data.User), Queries);

        public DeliveryReportService Report => new(Context);
    }

    private sealed record Setup(
        Env Env,
        OnlineOrderTestData.Priced Product,
        (Guid UserId, Guid MemberId, string Name) Driver,
        Guid EarlyLot,
        Guid LateLot);

    // A product with two lots (FEFO: early before late) and a DELIVERY_STAFF driver. The session acts as the store owner.
    private static async Task<Setup> PrepareAsync(RealDb.Session session, long early = 12, long late = 50)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var driver = await data.NewMemberAsync(RoleCode.DeliveryStaff);
        var earlyLot = await data.NewLotAsync(product.StoreProductId, $"E-{OnlineOrderTestData.Tag()}", early, Today.AddDays(30));
        var lateLot = await data.NewLotAsync(product.StoreProductId, $"L-{OnlineOrderTestData.Tag()}", late, Today.AddDays(90));
        data.ActAsStaff();

        return new Setup(new Env(data), product, driver, earlyLot.Id, lateLot.Id);
    }

    private static readonly DeliveryAddressRequest Address = new("Nguyen Van A", "0901234567", "Ap 3", "Can Tho");

    // A confirmed DELIVERY order of `bottles` bottles (walk-in unless a Farmer is given); stock reserved FEFO.
    private static async Task<OrderResponse> ConfirmedOrderAsync(Setup s, long bottles, Guid? farmerProfileId = null)
    {
        var order = await s.Env.Orders.CreateAsync(
            new CreateCounterOrderRequest(farmerProfileId is null ? "WALK_IN" : "REGISTERED", "FULL_PAYMENT", "DELIVERY",
                [new OrderItemRequest(s.Product.StoreProductId, s.Product.BottleId, bottles)], farmerProfileId,
                DeliveryAddress: Address),
            Token);

        return await s.Env.Confirmation.ConfirmAsync(order.Id, Token);
    }

    private static async Task<DeliveryResponse> DispatchedAsync(Setup s, Guid orderId, Guid orderItemId, long planned)
    {
        var delivery = await s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(orderId, [new(orderItemId, planned)]), Token);
        await s.Env.Deliveries.AssignAsync(delivery.Id, new(s.Driver.UserId), Token);

        return await s.Env.Deliveries.DispatchAsync(delivery.Id, Token);
    }

    private static CompleteAttemptRequest Delivered(DeliveryAttemptResponse attempt, long quantity) =>
        new([new DeliveredItemRequest(attempt.Items.Single().AllocationId, quantity)], "Người nhận", Proof);

    [RealDbFact]
    public async Task Planned_quantity_across_active_notes_is_limited_and_lots_follow_the_reservation_fefo()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 20); // reserved: 12 early + 8 late
        var item = order.Items.Single().Id;

        var first = await s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(order.Id, [new(item, 15)]), Token);
        Assert.Equal("DRAFT", first.Status);
        Assert.StartsWith("DL-", first.DeliveryNumber);
        Assert.Equal("Ap 3", first.DeliveryAddress.AddressLine); // the order's address
        var allocations = first.Items.Single().Allocations;
        Assert.Equal([(s.EarlyLot, 12L), (s.LateLot, 3L)], allocations.Select(a => (a.InventoryLotId, a.AllocatedBaseQuantity)));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(order.Id, [new(item, 6)]), Token));
        Assert.Contains("items[0]", error.Errors!.Keys);

        var second = await s.Env.Deliveries.CreateAsync(
            new CreateDeliveryRequest(order.Id, [new(item, 5)], new DeliveryAddressRequest("Tran B", "+84 912 345 678", "Ap 5", "Hau Giang")),
            Token);
        Assert.Equal((s.LateLot, 5L), (second.Items.Single().Allocations.Single().InventoryLotId, second.Items.Single().Allocations.Single().AllocatedBaseQuantity));
        Assert.Equal("0912345678", second.DeliveryAddress.RecipientPhone);

        // A cancelled note frees its quantity for a new note.
        await s.Env.Deliveries.CancelAsync(second.Id, new("Đổi lịch"), Token);
        await s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(order.Id, [new(item, 5)]), Token);
        Assert.Equal(3, (await s.Env.Deliveries.ListForOrderAsync(order.Id, Token)).Count);
    }

    [RealDbFact]
    public async Task A_note_is_only_for_a_confirmed_delivery_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var pending = await s.Env.Orders.CreateAsync(
            new CreateCounterOrderRequest("WALK_IN", "FULL_PAYMENT", "DELIVERY",
                [new OrderItemRequest(s.Product.StoreProductId, s.Product.BottleId, 2)], DeliveryAddress: Address), Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(pending.Id, [new(pending.Items.Single().Id, 2)]), Token));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(Guid.NewGuid(), [new(Guid.NewGuid(), 1)]), Token));
    }

    [RealDbFact]
    public async Task Changing_lots_keeps_the_old_allocation_as_history_and_moves_the_reservation()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 10); // all 10 reserved on the early lot
        var delivery = await s.Env.Deliveries.CreateAsync(new CreateDeliveryRequest(order.Id, [new(order.Items.Single().Id, 10)]), Token);
        var item = delivery.Items.Single();
        Assert.Equal((12L, 10L), await s.Env.Data.BalanceAsync(s.EarlyLot));

        // 4 stay on the early lot, 6 move to the late lot.
        var changed = await s.Env.Deliveries.ChangeLotsAsync(delivery.Id, item.Id,
            new ChangeLotsRequest([new(s.EarlyLot, 4), new(s.LateLot, 6)]), Token);

        var allocations = changed.Items.Single().Allocations;
        Assert.Equal(3, allocations.Count);
        Assert.Equal("RELEASED", allocations.Single(a => a.Id == item.Allocations.Single().Id).Status);
        Assert.Equal(6L, allocations.Single(a => a.InventoryLotId == s.LateLot).AllocatedBaseQuantity);
        Assert.Equal((12L, 4L), await s.Env.Data.BalanceAsync(s.EarlyLot));
        Assert.Equal((50L, 6L), await s.Env.Data.BalanceAsync(s.LateLot));
        var reserved = await s.Env.Data.Context.InventoryReservations.AsNoTracking().Include(r => r.Items)
            .SingleAsync(r => r.OrderId == order.Id, Token);
        Assert.Equal(10L, reserved.RemainingQuantity);
        Assert.Equal(6L, reserved.Items.Single(i => i.InventoryLotId == s.LateLot).RemainingQuantity);

        await Assert.ThrowsAsync<BusinessRuleException>(() => s.Env.Deliveries.ChangeLotsAsync(delivery.Id, item.Id,
            new ChangeLotsRequest([new(s.LateLot, 9)]), Token)); // must add up to the 10 not yet delivered
    }

    [RealDbFact]
    public async Task Delivery_staff_only_reach_the_deliveries_assigned_to_them()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var other = await s.Env.Data.NewMemberAsync(RoleCode.DeliveryStaff);
        var order = await ConfirmedOrderAsync(s, 4);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 4);

        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        Assert.Equal(delivery.Id, (await s.Env.Deliveries.GetAsync(delivery.Id, Token)).Id);
        Assert.Contains((await s.Env.Deliveries.ListAsync(new DeliveryListRequest(), Token)).Items, d => d.Id == delivery.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Env.Deliveries.DispatchAsync(delivery.Id, Token));

        s.Env.Data.ActAs(other.UserId, "DELIVERY_STAFF");
        await Assert.ThrowsAsync<NotFoundException>(() => s.Env.Deliveries.GetAsync(delivery.Id, Token));
        Assert.DoesNotContain((await s.Env.Deliveries.ListAsync(new DeliveryListRequest(), Token)).Items, d => d.Id == delivery.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Env.Attempts.StartAsync(delivery.Id, new(), Token));

        // Only DELIVERY_STAFF members can be assigned.
        s.Env.Data.ActAsStaff();
        var sales = await s.Env.Data.NewMemberAsync(RoleCode.SalesStaff);
        await Assert.ThrowsAsync<BusinessRuleException>(() => s.Env.Deliveries.AssignAsync(delivery.Id, new(sales.UserId), Token));
    }

    [RealDbFact]
    public async Task Design_example_20_delivered_as_18_then_2_completes_the_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session, early: 30);
        var order = await ConfirmedOrderAsync(s, 20);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 20);
        Assert.Equal("OUT_FOR_DELIVERY", delivery.Status);

        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var first = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);
        await Assert.ThrowsAsync<DomainException>(() => s.Env.Attempts.StartAsync(delivery.Id, new(), Token)); // one in progress
        Assert.Equal(20L, first.Items.Single().AttemptedBaseQuantity);

        var afterFirst = await s.Env.Attempts.CompleteAsync(delivery.Id, first.Id, Delivered(first, 18) with { FailureReasonCode = "customer_absent" }, Token);
        Assert.Equal("PARTIALLY_DELIVERED", afterFirst.Status);
        var attempt1 = afterFirst.Attempts.Single();
        Assert.Equal("PARTIAL_SUCCESS", attempt1.Status);
        Assert.Equal((18L, 2L), (attempt1.Items.Single().DeliveredBaseQuantity, attempt1.Items.Single().FailedBaseQuantity));
        Assert.Equal("CUSTOMER_ABSENT", attempt1.FailureReasonCode);
        var movement = await s.Env.Data.Context.StockMovements.AsNoTracking().Include(m => m.Items)
            .SingleAsync(m => m.Id == attempt1.SaleStockMovementId, Token);
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal(delivery.Id, movement.DeliveryId);
        Assert.Equal("PARTIALLY_FULFILLED", (await new OrderQueries(s.Env.Data.Context).GetAsync(order.Id, Token)).Status);
        Assert.Equal((12L, 2L), await s.Env.Data.BalanceAsync(s.EarlyLot)); // 30 - 18 on hand, 2 still reserved

        s.Env.Data.ActAsStaff();
        await s.Env.Deliveries.DispatchAsync(delivery.Id, Token);
        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var second = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);
        Assert.Equal(2L, second.Items.Single().AttemptedBaseQuantity);
        var done = await s.Env.Attempts.CompleteAsync(delivery.Id, second.Id, Delivered(second, 2), Token);

        Assert.Equal("DELIVERED", done.Status);
        Assert.Equal("DELIVERED", done.Items.Single().Status);
        Assert.Equal("COMPLETED", (await new OrderQueries(s.Env.Data.Context).GetAsync(order.Id, Token)).Status);
        Assert.Equal((10L, 0L), await s.Env.Data.BalanceAsync(s.EarlyLot));
    }

    [RealDbFact]
    public async Task Proof_and_receiver_are_required_and_only_this_stores_photos_are_accepted()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 3);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 3);
        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var attempt = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, Delivered(attempt, 3) with { ProofImageUrl = null }, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, Delivered(attempt, 3) with { ReceiverName = " " }, Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, Delivered(attempt, 3) with { ProofImageUrl = "https://evil.example/x.jpg" }, Token));

        await s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, Delivered(attempt, 3), Token);
        Assert.True(await new DeliveryProofUsage(s.Env.Data.Context).IsUsedAsync(Proof["https://cdn.example.com/".Length..], Token));
    }

    [RealDbFact]
    public async Task A_failed_attempt_posts_nothing_and_waits_for_a_retry()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 5);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 5);
        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var attempt = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, new CompleteAttemptRequest(), Token)); // reason required
        var after = await s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, new CompleteAttemptRequest(FailureReasonCode: "UNREACHABLE"), Token);

        Assert.Equal("RETRY_PENDING", after.Status);
        Assert.Equal("FAILED", after.Attempts.Single().Status);
        Assert.Null(after.Attempts.Single().SaleStockMovementId);
        Assert.False(await s.Env.Data.Context.StockMovements.AsNoTracking().AnyAsync(m => m.OrderId == order.Id, Token));
        Assert.Equal((12L, 5L), await s.Env.Data.BalanceAsync(s.EarlyLot));
        Assert.Equal("CONFIRMED", (await new OrderQueries(s.Env.Data.Context).GetAsync(order.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task Two_trips_complete_the_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 20);
        var item = order.Items.Single().Id;

        foreach (var planned in new long[] { 12, 8 })
        {
            var trip = await DispatchedAsync(s, order.Id, item, planned);
            s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
            var attempt = await s.Env.Attempts.StartAsync(trip.Id, new(), Token);
            var total = attempt.Items.Sum(i => i.AttemptedBaseQuantity);
            var done = await s.Env.Attempts.CompleteAsync(trip.Id, attempt.Id,
                new(attempt.Items.Select(i => new DeliveredItemRequest(i.AllocationId, i.AttemptedBaseQuantity)).ToList(), "Người nhận", Proof),
                Token);
            Assert.Equal("DELIVERED", done.Status);
            Assert.Equal(planned, total);
            s.Env.Data.ActAsStaff();
        }

        Assert.Equal("COMPLETED", (await new OrderQueries(s.Env.Data.Context).GetAsync(order.Id, Token)).Status);
        Assert.Equal(2, await s.Env.Data.Context.StockMovements.AsNoTracking().CountAsync(m => m.OrderId == order.Id, Token));
    }

    [RealDbFact]
    public async Task Incidents_are_recorded_and_only_a_posted_adjustment_can_be_linked()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 4);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 4);
        var allocation = delivery.Items.Single().Allocations.Single().Id;

        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var incident = await s.Env.Incidents.ReportAsync(delivery.Id,
            new ReportIncidentRequest("damaged", "Rách 1 bao", AllocationId: allocation, AffectedBaseQuantity: 1, EvidenceImageUrl: Proof), Token);
        Assert.Equal("DAMAGED", incident.IncidentType);
        Assert.Equal("OPEN", incident.Status);
        await Assert.ThrowsAsync<BusinessRuleException>(() => s.Env.Incidents.ReportAsync(delivery.Id,
            new ReportIncidentRequest("DAMAGED", "Quá số lượng", AllocationId: allocation, AffectedBaseQuantity: 5), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            s.Env.Incidents.ResolveAsync(delivery.Id, incident.Id, new("NO_ACTION"), Token)); // Operate only

        s.Env.Data.ActAsStaff();
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            s.Env.Incidents.ResolveAsync(delivery.Id, incident.Id, new("WRITE_OFF", RelatedStockMovementId: Guid.NewGuid()), Token));
        var resolved = await s.Env.Incidents.ResolveAsync(delivery.Id, incident.Id, new("NO_ACTION", "Khách vẫn nhận"), Token);
        Assert.Equal("RESOLVED", resolved.Status);
        Assert.Equal("NO_ACTION", resolved.ResolutionType);
        Assert.Single(await s.Env.Incidents.ListAsync(delivery.Id, Token));
    }

    [RealDbFact]
    public async Task A_farmer_follows_only_their_own_orders_deliveries_in_packaging_units()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var farmer = await s.Env.Data.ActAsNewFarmerAsync();
        s.Env.Data.ActAsStaff();
        var order = await s.Env.Orders.CreateAsync(
            new CreateCounterOrderRequest("REGISTERED", "FULL_PAYMENT", "DELIVERY",
                [new OrderItemRequest(s.Product.StoreProductId, s.Product.BoxId, 2)], farmer.ProfileId, DeliveryAddress: Address), Token);
        await s.Env.Data.NewLotAsync(s.Product.StoreProductId, $"B-{OnlineOrderTestData.Tag()}", 60, Today.AddDays(60));
        order = await s.Env.Confirmation.ConfirmAsync(order.Id, Token);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 2);
        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var attempt = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);
        // One box (6 base units) delivered, split over the lots FEFO chose.
        var left = 6L;
        var delivered = attempt.Items.Select(i => { var q = Math.Min(left, i.AttemptedBaseQuantity); left -= q; return new DeliveredItemRequest(i.AllocationId, q); }).ToList();
        await s.Env.Attempts.CompleteAsync(delivery.Id, attempt.Id, new(delivered, "Người nhận", Proof, "CUSTOMER_ABSENT"), Token);

        s.Env.Data.ActAs(farmer.UserId, "FARMER");
        var mine = Assert.Single(await s.Env.Mine.ListForMyOrderAsync(order.Id, Token));
        Assert.Equal("PARTIALLY_DELIVERED", mine.Status);
        Assert.Equal(s.Driver.Name, mine.AssignedTo!.FullName);
        var line = mine.Items.Single();
        Assert.Equal((2L, 1L, 1L), (line.PlannedQuantity, line.DeliveredQuantity, line.RemainingQuantity));
        Assert.Equal(Proof, mine.Attempts.Single().ProofImageUrl);

        await s.Env.Data.ActAsNewFarmerAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => s.Env.Mine.ListForMyOrderAsync(order.Id, Token));
    }

    [RealDbFact]
    public async Task The_report_counts_attempts_per_staff_and_failure_reasons()
    {
        await using var session = await RealDb.Session.StartAsync();
        var s = await PrepareAsync(session);
        var order = await ConfirmedOrderAsync(s, 6);
        var delivery = await DispatchedAsync(s, order.Id, order.Items.Single().Id, 6);

        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var failed = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);
        await s.Env.Attempts.CompleteAsync(delivery.Id, failed.Id, new CompleteAttemptRequest(FailureReasonCode: "WEATHER"), Token);
        s.Env.Data.ActAsStaff();
        await s.Env.Deliveries.DispatchAsync(delivery.Id, Token);
        s.Env.Data.ActAs(s.Driver.UserId, "DELIVERY_STAFF");
        var ok = await s.Env.Attempts.StartAsync(delivery.Id, new(), Token);
        await s.Env.Attempts.CompleteAsync(delivery.Id, ok.Id, Delivered(ok, 6), Token);

        var today = BusinessCalendar.Today(DateTimeOffset.UtcNow);
        var report = await s.Env.Report.GetAsync(new DeliveryReportRequest { FromDate = today, ToDate = today, GroupBy = "staff" }, Token);
        var row = report.Rows.Single(r => r.Key == s.Driver.UserId.ToString());
        Assert.Equal((s.Driver.Name, 1, 2, 1, 0, 1, 0.5m), (row.Label, row.Deliveries, row.Attempts, row.Successful, row.Partial, row.Failed, row.SuccessRate));
        Assert.Contains(report.FailureReasons, r => r.Code == "WEATHER" && r.Count >= 1);
        Assert.Equal("STAFF", report.GroupBy);

        var byDay = await s.Env.Report.GetAsync(new DeliveryReportRequest { FromDate = today, ToDate = today }, Token);
        Assert.Equal(today.ToString("yyyy-MM-dd"), Assert.Single(byDay.Rows).Key);
    }
}
