using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Deliveries.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Deliveries;

// F2.8 (FLOW_2 §9a, DATABASE_DESIGN §35.26): the driver's phone sends its position while a delivery attempt runs; store
// staff see running trips and routes, the order's Farmer sees the running trip only. Recording changes nothing else (no
// status, stock, debt, notification or audit log) and is never required to start or complete an attempt.
public sealed class DeliveryLocationService(
    IAgriSageDbContext context,
    DeliveryAccess access,
    DeliveryQueries queries,
    CurrentFarmer currentFarmer,
    IDateTimeProvider clock) : IDeliveryLocationService
{
    // A route returns at most the latest points.
    public const int MaxRoutePoints = 2000;

    // Phone clocks drift: a point may be a little older than the attempt or a little ahead of the server.
    public static readonly TimeSpan EarlyTolerance = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(2);

    public async Task<RecordLocationsResponse> RecordAsync(
        Guid deliveryId, Guid attemptId, RecordLocationsRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        await access.EnsureVisibleAsync(actor, deliveryId, cancellationToken);
        var attempt = await context.DeliveryAttempts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attemptId && a.DeliveryId == deliveryId, cancellationToken)
            ?? throw new NotFoundException("Delivery attempt", attemptId);

        // Only the store member who started the attempt carries the phone of this trip.
        var isDriver = await context.StoreMembers.AsNoTracking()
            .AnyAsync(m => m.Id == attempt.AttemptedByMemberId && m.UserId == actor.UserId, cancellationToken);
        if (!isDriver)
        {
            throw new ForbiddenException("Only the staff member who started this delivery attempt shares its location.");
        }

        if (attempt.Status != DeliveryAttemptStatus.InProgress)
        {
            throw new BusinessRuleException(
                $"Delivery attempt #{attempt.AttemptNumber} is {EnumText.Format(attempt.Status)}: its location is no longer recorded.");
        }

        var earliest = attempt.StartedAt - EarlyTolerance;
        var latest = clock.UtcNow + FutureTolerance;
        // Within one batch the first point of a given time wins (GroupBy keeps the order of first appearance).
        var inWindow = request.Points
            .Select(p => p with { RecordedAt = DeliveryLocationPoint.NormalizeTime(p.RecordedAt) })
            .Where(p => p.RecordedAt >= earliest && p.RecordedAt <= latest)
            .GroupBy(p => p.RecordedAt)
            .Select(g => g.First())
            .ToList();

        var times = inWindow.Select(p => p.RecordedAt).ToList();
        var stored = (await context.DeliveryLocationPoints.AsNoTracking()
                .Where(p => p.DeliveryAttemptId == attemptId && times.Contains(p.RecordedAt))
                .Select(p => p.RecordedAt)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var fresh = inWindow.Where(p => !stored.Contains(p.RecordedAt)).ToList();

        if (fresh.Count > 0)
        {
            context.DeliveryLocationPoints.AddRange(fresh.Select(p =>
                DeliveryLocationPoint.Record(attempt, p.Latitude, p.Longitude, p.AccuracyMeters, p.RecordedAt)));
            await context.SaveChangesAsync(cancellationToken);
        }

        return new RecordLocationsResponse(fresh.Count, request.Points.Count - fresh.Count);
    }

    public async Task<IReadOnlyList<LocationPointResponse>> RouteAsync(Guid deliveryId, Guid attemptId, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        await access.EnsureVisibleAsync(actor, deliveryId, cancellationToken);
        if (!await context.DeliveryAttempts.AsNoTracking().AnyAsync(a => a.Id == attemptId && a.DeliveryId == deliveryId, cancellationToken))
        {
            throw new NotFoundException("Delivery attempt", attemptId);
        }

        return await RoutePointsAsync(attemptId, cancellationToken);
    }

    public async Task<IReadOnlyList<LiveDeliveryResponse>> LiveAsync(CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        var rows = await (
                from delivery in access.Scope(actor).AsNoTracking()
                from attempt in delivery.Attempts
                where attempt.Status == DeliveryAttemptStatus.InProgress
                orderby attempt.StartedAt, delivery.DeliveryNumber
                select new
                {
                    delivery.Id, delivery.DeliveryNumber, delivery.OrderId,
                    OrderNumber = context.Orders.Where(o => o.Id == delivery.OrderId).Select(o => o.OrderNumber).First(),
                    delivery.RecipientNameSnapshot, delivery.AddressLineSnapshot, delivery.LatitudeSnapshot,
                    delivery.LongitudeSnapshot, AttemptId = attempt.Id, attempt.AttemptNumber, attempt.StartedAt,
                    attempt.AttemptedByMemberId,
                    Last = context.DeliveryLocationPoints
                        .Where(p => p.DeliveryAttemptId == attempt.Id)
                        .OrderByDescending(p => p.RecordedAt)
                        .Select(p => new { p.Latitude, p.Longitude, p.AccuracyMeters, p.RecordedAt })
                        .FirstOrDefault()
                })
            .ToListAsync(cancellationToken);
        var drivers = await queries.StaffAsync(rows.Select(r => r.AttemptedByMemberId).Distinct().ToList(), cancellationToken);

        return rows.Select(r => new LiveDeliveryResponse(
            r.Id, r.DeliveryNumber, r.OrderId, r.OrderNumber, r.RecipientNameSnapshot, r.AddressLineSnapshot,
            Destination(r.LatitudeSnapshot, r.LongitudeSnapshot), r.AttemptId, r.AttemptNumber, r.StartedAt,
            drivers.GetValueOrDefault(r.AttemptedByMemberId),
            r.Last is null ? null : new LocationPointResponse(r.Last.Latitude, r.Last.Longitude, r.Last.AccuracyMeters, r.Last.RecordedAt)))
            .ToList();
    }

    public async Task<MyDeliveryTrackingResponse> MyTrackingAsync(Guid orderId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var farmer = await currentFarmer.GetAsync(cancellationToken);
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        // Someone else's order, or a delivery of another order, is "not found" (as for F2.7); drafts are not shown.
        var delivery = await context.Deliveries.AsNoTracking()
                .Where(d => d.Id == deliveryId && d.OrderId == orderId && d.Status != DeliveryStatus.Draft
                    && context.Orders.Any(o => o.Id == orderId && o.StoreId == storeId && o.FarmerProfileId == farmer.FarmerProfileId))
                .Select(d => new
                {
                    d.Id, d.DeliveryNumber, d.Status, d.LatitudeSnapshot, d.LongitudeSnapshot,
                    Running = d.Attempts.Where(a => a.Status == DeliveryAttemptStatus.InProgress)
                        .Select(a => new { a.Id, a.AttemptNumber, a.StartedAt, a.AttemptedByMemberId })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Delivery", deliveryId);
        var destination = Destination(delivery.LatitudeSnapshot, delivery.LongitudeSnapshot);

        if (delivery.Running is null)
        {
            // The Farmer does not see finished trips.
            return new MyDeliveryTrackingResponse(delivery.Id, delivery.DeliveryNumber, EnumText.Format(delivery.Status), false,
                null, null, null, destination, null, []);
        }

        var drivers = await queries.StaffAsync([delivery.Running.AttemptedByMemberId], cancellationToken);
        var trail = await RoutePointsAsync(delivery.Running.Id, cancellationToken);

        return new MyDeliveryTrackingResponse(
            delivery.Id, delivery.DeliveryNumber, EnumText.Format(delivery.Status), true, delivery.Running.AttemptNumber,
            delivery.Running.StartedAt,
            drivers.TryGetValue(delivery.Running.AttemptedByMemberId, out var driver)
                ? new MyDeliveryStaff(driver.FullName, driver.PhoneNumber)
                : null,
            destination, trail.Count > 0 ? trail[^1] : null, trail);
    }

    // The latest MaxRoutePoints points of an attempt, in time order.
    private async Task<IReadOnlyList<LocationPointResponse>> RoutePointsAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        var points = await context.DeliveryLocationPoints.AsNoTracking()
            .Where(p => p.DeliveryAttemptId == attemptId)
            .OrderByDescending(p => p.RecordedAt)
            .Take(MaxRoutePoints)
            .Select(p => new LocationPointResponse(p.Latitude, p.Longitude, p.AccuracyMeters, p.RecordedAt))
            .ToListAsync(cancellationToken);
        points.Reverse();

        return points;
    }

    private static GeoPoint? Destination(decimal? latitude, decimal? longitude) =>
        latitude is { } lat && longitude is { } lng ? new GeoPoint(lat, lng) : null;
}
