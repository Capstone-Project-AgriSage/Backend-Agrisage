using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Enums;

namespace AgriSage.Domain.Features.Deliveries.Entities;

// delivery_location_points (DATABASE_DESIGN §35.26, F2.8): one position of the driver's phone during an IN_PROGRESS
// delivery attempt. Append-only: created here, never changed or deleted. It changes nothing else.
public sealed class DeliveryLocationPoint : AuditableEntity
{
    // A phone reports its horizontal accuracy in meters; anything beyond this is not a position.
    public const decimal MaxAccuracyMeters = 10000m;

    private DeliveryLocationPoint()
    {
    }

    private DeliveryLocationPoint(Guid deliveryAttemptId, decimal latitude, decimal longitude, decimal? accuracyMeters,
        DateTimeOffset recordedAt)
    {
        DeliveryAttemptId = deliveryAttemptId;
        Latitude = latitude;
        Longitude = longitude;
        AccuracyMeters = accuracyMeters;
        RecordedAt = recordedAt;
    }

    public Guid DeliveryAttemptId { get; private set; }

    public decimal Latitude { get; private set; }

    public decimal Longitude { get; private set; }

    public decimal? AccuracyMeters { get; private set; }

    // When the phone took the position (UTC); CreatedAt is when the server received it.
    public DateTimeOffset RecordedAt { get; private set; }

    // A position is recorded only while the attempt is running. Coordinates keep 7 decimals (about 1 cm), the accuracy 2.
    public static DeliveryLocationPoint Record(
        DeliveryAttempt attempt, decimal latitude, decimal longitude, decimal? accuracyMeters, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Status != DeliveryAttemptStatus.InProgress)
        {
            throw new DomainException(
                $"Delivery attempt #{attempt.AttemptNumber} is {attempt.Status}: its location is no longer recorded.");
        }

        if (latitude is < -90m or > 90m)
        {
            throw new DomainException("Latitude must be between -90 and 90.");
        }

        if (longitude is < -180m or > 180m)
        {
            throw new DomainException("Longitude must be between -180 and 180.");
        }

        if (accuracyMeters is < 0m or > MaxAccuracyMeters)
        {
            throw new DomainException($"Accuracy must be between 0 and {MaxAccuracyMeters} meters.");
        }

        return new DeliveryLocationPoint(
            attempt.Id,
            Math.Round(latitude, 7, MidpointRounding.AwayFromZero),
            Math.Round(longitude, 7, MidpointRounding.AwayFromZero),
            accuracyMeters is { } accuracy ? Math.Round(accuracy, 2, MidpointRounding.AwayFromZero) : null,
            NormalizeTime(recordedAt));
    }

    // UTC, whole milliseconds: what PostgreSQL stores exactly, so a resent point compares equal to the stored one.
    public static DateTimeOffset NormalizeTime(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }
}
