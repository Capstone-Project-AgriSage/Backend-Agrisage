using AgriSage.Domain.Features.Deliveries.Entities;
using FluentValidation;

namespace AgriSage.Application.Features.Deliveries;

// F2.8 (FLOW_2 §9a): the driver's position while a delivery attempt is IN_PROGRESS. Points are append-only.

public sealed record LocationPointRequest(decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);

public sealed record RecordLocationsRequest(IReadOnlyList<LocationPointRequest> Points);

// Ignored = outside the attempt's time window or already recorded (a resent batch adds nothing).
public sealed record RecordLocationsResponse(int Accepted, int Ignored);

public sealed record LocationPointResponse(decimal Latitude, decimal Longitude, decimal? AccuracyMeters, DateTimeOffset RecordedAt);

public sealed record GeoPoint(decimal Latitude, decimal Longitude);

// One IN_PROGRESS attempt for the store's map; Destination is the delivery's address snapshot (null without coordinates).
public sealed record LiveDeliveryResponse(
    Guid DeliveryId,
    string DeliveryNumber,
    Guid OrderId,
    string OrderNumber,
    string RecipientName,
    string AddressLine,
    GeoPoint? Destination,
    Guid AttemptId,
    int AttemptNumber,
    DateTimeOffset StartedAt,
    DeliveryStaffReference? Driver,
    LocationPointResponse? LastPoint);

// The Farmer's view of one delivery of their order: positions only while an attempt runs.
public sealed record MyDeliveryTrackingResponse(
    Guid DeliveryId,
    string DeliveryNumber,
    string Status,
    bool InProgress,
    int? AttemptNumber,
    DateTimeOffset? StartedAt,
    MyDeliveryStaff? Driver,
    GeoPoint? Destination,
    LocationPointResponse? LastPoint,
    IReadOnlyList<LocationPointResponse> Trail);

public interface IDeliveryLocationService
{
    Task<RecordLocationsResponse> RecordAsync(Guid deliveryId, Guid attemptId, RecordLocationsRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationPointResponse>> RouteAsync(Guid deliveryId, Guid attemptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LiveDeliveryResponse>> LiveAsync(CancellationToken cancellationToken);

    Task<MyDeliveryTrackingResponse> MyTrackingAsync(Guid orderId, Guid deliveryId, CancellationToken cancellationToken);
}

public sealed class RecordLocationsRequestValidator : AbstractValidator<RecordLocationsRequest>
{
    public const int MaxPoints = 100;

    public RecordLocationsRequestValidator()
    {
        RuleFor(r => r.Points).NotEmpty()
            .Must(points => points is null || points.Count <= MaxPoints)
            .WithMessage($"At most {MaxPoints} points per request.");
        RuleForEach(r => r.Points).ChildRules(point =>
        {
            point.RuleFor(p => p.Latitude).InclusiveBetween(-90m, 90m);
            point.RuleFor(p => p.Longitude).InclusiveBetween(-180m, 180m);
            point.RuleFor(p => p.AccuracyMeters).InclusiveBetween(0m, DeliveryLocationPoint.MaxAccuracyMeters)
                .When(p => p.AccuracyMeters is not null);
            point.RuleFor(p => p.RecordedAt).NotEqual(default(DateTimeOffset)).WithMessage("recordedAt is required.");
        }).When(r => r.Points is not null);
    }
}
