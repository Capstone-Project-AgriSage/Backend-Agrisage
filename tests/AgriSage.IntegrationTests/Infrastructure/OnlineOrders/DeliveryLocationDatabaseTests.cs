using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Deliveries;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using AgriSage.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.OnlineOrders;

// FLOW_2 §9a (task F2.8) against PostgreSQL; every session rolls back. Covers the "must prove" row of §10: only the
// attempt's driver records, only while IN_PROGRESS; a resent batch adds no row and out-of-window points are ignored; live
// lists running attempts in scope; the Farmer sees only their own running trip; the table's checks and unique index hold.
[Collection(RealDb.WalkInPriceListCollection)]
public class DeliveryLocationDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private const string Proof = "https://cdn.example.com/2026/11/0123456789abcdef0123456789abcdef.jpg";

    // The farm the delivery goes to (Can Tho).
    private const decimal FarmLatitude = 10.0452101m;
    private const decimal FarmLongitude = 105.7469201m;

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

        public AgriSageDbContext Context => Data.Context;

        private AuditTrail Audit => new(Context, Data.User, _clock);

        private RowLockService Locks => new(Context);

        private OrderQueries OrderQueries => new(Context);

        private DeliveryAccess Access => new(Context, Data.User);

        private DeliveryQueries Queries => new(Context);

        public OrderService Orders => new(Context, new OrderBuilder(Context, new PriceResolver(Context), Data.User, _clock, Audit,
                new AgriSage.Application.Features.Credit.CreditEligibilityService(Context, _clock)),
            OrderQueries, Data.User, _clock, _errors, Audit);

        public OrderConfirmationService Confirmation => new(Context, Locks,
            new OrderConfirmer(Context, Locks, new StubOrderSettlementGuard(), Data.User, _clock, Audit), OrderQueries, _clock,
            _errors, Audit);

        public DeliveryService Deliveries => new(Context, Access, Queries, Locks, _clock, _errors, Audit);

        public DeliveryAttemptService Attempts => new(Context, Access, Queries, new DeliveryProofUrls(new FakeStorage()),
            new FulfillmentPostingService(Context, Locks, new StubFulfillmentFinancialPosting(new OrderPrepaymentLedger(Context)),
                new OrderPaymentCancellation(Context, Locks, new FakePaymentGateway(), _clock, Audit)),
            Locks, _clock, Audit);

        public DeliveryLocationService Locations => new(Context, Access, Queries, new CurrentFarmer(Context, Data.User), _clock);
    }

    private sealed record Trip(Env Env, (Guid UserId, Guid MemberId, string Name) Driver, OrderResponse Order, DeliveryResponse Delivery);

    // A confirmed DELIVERY order of 2 boxes to the farm (registered when a Farmer is given), dispatched to a DELIVERY_STAFF
    // driver. The session acts as the store owner.
    private static async Task<Trip> DispatchedTripAsync(RealDb.Session session, bool forFarmer = false)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var product = await data.NewProductAsync();
        var driver = await data.NewMemberAsync(RoleCode.DeliveryStaff);
        await data.NewLotAsync(product.StoreProductId, $"T-{OnlineOrderTestData.Tag()}", 60, Today.AddDays(60));
        var farmer = forFarmer ? await data.ActAsNewFarmerAsync() : null;
        data.ActAsStaff();
        var env = new Env(data);

        var address = new DeliveryAddressRequest("Nguyen Van A", "0901234567", "Ap 3", "Can Tho",
            Latitude: FarmLatitude, Longitude: FarmLongitude);
        var order = await env.Orders.CreateAsync(
            new CreateCounterOrderRequest(farmer is null ? "WALK_IN" : "REGISTERED", "FULL_PAYMENT", "DELIVERY",
                [new OrderItemRequest(product.StoreProductId, product.BoxId, 2)], farmer?.ProfileId, DeliveryAddress: address),
            Token);
        order = await env.Confirmation.ConfirmAsync(order.Id, Token);
        var delivery = await env.Deliveries.CreateAsync(new CreateDeliveryRequest(order.Id, [new(order.Items.Single().Id, 2)]), Token);
        await env.Deliveries.AssignAsync(delivery.Id, new(driver.UserId), Token);
        delivery = await env.Deliveries.DispatchAsync(delivery.Id, Token);

        if (farmer is not null)
        {
            env.Data.ActAs(farmer.UserId, "FARMER");
        }

        return new Trip(env, driver, order, delivery);
    }

    private static async Task<DeliveryAttemptResponse> DriverStartsAsync(Trip t)
    {
        t.Env.Data.ActAs(t.Driver.UserId, "DELIVERY_STAFF");
        return await t.Env.Attempts.StartAsync(t.Delivery.Id, new(), Token);
    }

    private static Task<RecordLocationsResponse> SendAsync(Trip t, Guid attemptId, params LocationPointRequest[] points) =>
        t.Env.Locations.RecordAsync(t.Delivery.Id, attemptId, new RecordLocationsRequest(points), Token);

    private static LocationPointRequest Point(DateTimeOffset at, decimal latitude = 10.03m, decimal longitude = 105.76m) =>
        new(latitude, longitude, 8.5m, at);

    // Rounded to whole seconds so the expected times are exact after the millisecond normalisation.
    private static DateTimeOffset Now()
    {
        var utc = DateTimeOffset.UtcNow;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    private static async Task CompleteAsync(Trip t, DeliveryAttemptResponse attempt)
    {
        t.Env.Data.ActAs(t.Driver.UserId, "DELIVERY_STAFF");
        await t.Env.Attempts.CompleteAsync(t.Delivery.Id, attempt.Id,
            new([.. attempt.Items.Select(i => new DeliveredItemRequest(i.AllocationId, i.AttemptedBaseQuantity))], "Người nhận", Proof),
            Token);
    }

    [RealDbFact]
    public async Task The_driver_records_a_route_once_and_staff_read_it_in_time_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        var t = await DispatchedTripAsync(session);
        var attempt = await DriverStartsAsync(t);
        var now = Now();

        // Sent out of order, with one point twice in the batch, one far in the future and one long before the trip.
        var batch = new[]
        {
            Point(now.AddSeconds(-20), 10.0400001m), Point(now.AddSeconds(-60), 10.0300001m), Point(now.AddSeconds(-40), 10.0350001m),
            Point(now.AddSeconds(-40), 11m), Point(now.AddMinutes(10)), Point(now.AddDays(-1))
        };
        Assert.Equal(new RecordLocationsResponse(3, 3), await SendAsync(t, attempt.Id, batch));
        // A resent batch adds nothing.
        Assert.Equal(new RecordLocationsResponse(0, 6), await SendAsync(t, attempt.Id, batch));
        Assert.Equal(3, await t.Env.Context.DeliveryLocationPoints.CountAsync(p => p.DeliveryAttemptId == attempt.Id, Token));

        t.Env.Data.ActAsStaff();
        var route = await t.Env.Locations.RouteAsync(t.Delivery.Id, attempt.Id, Token);
        Assert.Equal([10.0300001m, 10.0350001m, 10.0400001m], route.Select(p => p.Latitude));
        Assert.Equal([now.AddSeconds(-60), now.AddSeconds(-40), now.AddSeconds(-20)], route.Select(p => p.RecordedAt));

        var live = Assert.Single(await t.Env.Locations.LiveAsync(Token), l => l.AttemptId == attempt.Id);
        Assert.Equal((t.Delivery.DeliveryNumber, t.Order.OrderNumber, "Nguyen Van A", 1),
            (live.DeliveryNumber, live.OrderNumber, live.RecipientName, live.AttemptNumber));
        Assert.Equal(new GeoPoint(FarmLatitude, FarmLongitude), live.Destination);
        Assert.Equal(t.Driver.Name, live.Driver!.FullName);
        Assert.Equal(new LocationPointResponse(10.0400001m, 105.76m, 8.5m, now.AddSeconds(-20)), live.LastPoint);
    }

    [RealDbFact]
    public async Task Only_the_driver_of_a_running_attempt_records_and_the_route_stays_after_it_ends()
    {
        await using var session = await RealDb.Session.StartAsync();
        var t = await DispatchedTripAsync(session);
        var attempt = await DriverStartsAsync(t);
        var now = Now();

        // Staff who can see the delivery did not start the trip.
        t.Env.Data.ActAsStaff();
        await Assert.ThrowsAsync<ForbiddenException>(() => SendAsync(t, attempt.Id, Point(now)));
        // Another driver does not even see the delivery; an attempt of another delivery is not found either.
        var other = await t.Env.Data.NewMemberAsync(RoleCode.DeliveryStaff);
        t.Env.Data.ActAs(other.UserId, "DELIVERY_STAFF");
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(t, attempt.Id, Point(now)));
        Assert.DoesNotContain(await t.Env.Locations.LiveAsync(Token), l => l.AttemptId == attempt.Id);
        t.Env.Data.ActAs(t.Driver.UserId, "DELIVERY_STAFF");
        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(t, Guid.NewGuid(), Point(now)));

        // No point yet: the trip is live without a position.
        Assert.Null(Assert.Single(await t.Env.Locations.LiveAsync(Token), l => l.AttemptId == attempt.Id).LastPoint);
        Assert.Equal(1, (await SendAsync(t, attempt.Id, Point(now.AddSeconds(-5)))).Accepted);

        await CompleteAsync(t, attempt);
        await Assert.ThrowsAsync<BusinessRuleException>(() => SendAsync(t, attempt.Id, Point(now)));
        t.Env.Data.ActAsStaff();
        Assert.DoesNotContain(await t.Env.Locations.LiveAsync(Token), l => l.AttemptId == attempt.Id);
        Assert.Single(await t.Env.Locations.RouteAsync(t.Delivery.Id, attempt.Id, Token));
    }

    [RealDbFact]
    public async Task A_farmer_sees_only_the_running_trip_of_their_own_order()
    {
        await using var session = await RealDb.Session.StartAsync();
        var t = await DispatchedTripAsync(session, forFarmer: true);
        var farmer = (t.Env.Data.User.UserId!.Value, t.Env.Data.User.Role!);

        // Dispatched but not started: nothing to show yet.
        var before = await t.Env.Locations.MyTrackingAsync(t.Order.Id, t.Delivery.Id, Token);
        Assert.False(before.InProgress);
        Assert.Null(before.LastPoint);
        Assert.Empty(before.Trail);
        Assert.Equal(new GeoPoint(FarmLatitude, FarmLongitude), before.Destination);

        var attempt = await DriverStartsAsync(t);
        var now = Now();
        await SendAsync(t, attempt.Id, Point(now.AddSeconds(-30), 10.01m), Point(now.AddSeconds(-10), 10.02m));

        t.Env.Data.ActAs(farmer.Item1, farmer.Item2);
        var during = await t.Env.Locations.MyTrackingAsync(t.Order.Id, t.Delivery.Id, Token);
        Assert.True(during.InProgress);
        Assert.Equal((1, t.Driver.Name), (during.AttemptNumber, during.Driver!.FullName));
        Assert.Equal([10.01m, 10.02m], during.Trail.Select(p => p.Latitude));
        Assert.Equal(10.02m, during.LastPoint!.Latitude);

        // A delivery of another order, or another Farmer's order, is not found.
        await Assert.ThrowsAsync<NotFoundException>(() => t.Env.Locations.MyTrackingAsync(Guid.NewGuid(), t.Delivery.Id, Token));
        await t.Env.Data.ActAsNewFarmerAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => t.Env.Locations.MyTrackingAsync(t.Order.Id, t.Delivery.Id, Token));

        // Once the trip ends the Farmer no longer sees the driver's position.
        await CompleteAsync(t, attempt);
        t.Env.Data.ActAs(farmer.Item1, farmer.Item2);
        var after = await t.Env.Locations.MyTrackingAsync(t.Order.Id, t.Delivery.Id, Token);
        Assert.Equal(("DELIVERED", false), (after.Status, after.InProgress));
        Assert.Null(after.LastPoint);
        Assert.Empty(after.Trail);
    }

    [RealDbFact]
    public async Task The_table_refuses_impossible_positions_and_a_second_point_at_the_same_time()
    {
        await using var session = await RealDb.Session.StartAsync();
        var t = await DispatchedTripAsync(session);
        var attempt = await DriverStartsAsync(t);
        var at = Now().AddSeconds(-1);
        await SendAsync(t, attempt.Id, Point(at));
        var db = t.Env.Context.Database;
        var transaction = db.CurrentTransaction!;

        async Task<PostgresException> RefusedAsync(FormattableString sql)
        {
            await transaction.CreateSavepointAsync("location_check", Token);
            var refused = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteSqlInterpolatedAsync(sql, Token));
            await transaction.RollbackToSavepointAsync("location_check", Token);
            return refused;
        }

        Assert.Equal("ck_delivery_location_points_latitude", (await RefusedAsync(
            $"INSERT INTO delivery_location_points (id, delivery_attempt_id, latitude, longitude, recorded_at, created_at, updated_at) VALUES (gen_random_uuid(), {attempt.Id}, 91, 105, now(), now(), now())")).ConstraintName);
        Assert.Equal("ck_delivery_location_points_longitude", (await RefusedAsync(
            $"INSERT INTO delivery_location_points (id, delivery_attempt_id, latitude, longitude, recorded_at, created_at, updated_at) VALUES (gen_random_uuid(), {attempt.Id}, 10, -181, now(), now(), now())")).ConstraintName);
        Assert.Equal("ck_delivery_location_points_accuracy_meters", (await RefusedAsync(
            $"INSERT INTO delivery_location_points (id, delivery_attempt_id, latitude, longitude, accuracy_meters, recorded_at, created_at, updated_at) VALUES (gen_random_uuid(), {attempt.Id}, 10, 105, -1, now(), now(), now())")).ConstraintName);
        var duplicate = await RefusedAsync(
            $"INSERT INTO delivery_location_points (id, delivery_attempt_id, latitude, longitude, recorded_at, created_at, updated_at) VALUES (gen_random_uuid(), {attempt.Id}, 10, 105, {at}, now(), now())");
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("ux_delivery_location_points_delivery_attempt_id_recorded_at", duplicate.ConstraintName);
    }
}
