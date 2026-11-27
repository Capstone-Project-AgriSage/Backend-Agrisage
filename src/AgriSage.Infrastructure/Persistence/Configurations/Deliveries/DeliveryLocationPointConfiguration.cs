using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Deliveries;

// delivery_location_points (DATABASE_DESIGN §35.26, F2.8): the driver's positions during an IN_PROGRESS attempt,
// append-only. The unique (attempt, recorded_at) index makes a resent batch add no row and serves routes in time order.
internal sealed class DeliveryLocationPointConfiguration : EntityConfiguration<DeliveryLocationPoint>
{
    protected override string TableName => "delivery_location_points";

    protected override void ConfigureEntity(EntityTypeBuilder<DeliveryLocationPoint> builder)
    {
        builder.Property(point => point.Latitude).IsCoordinate();
        builder.Property(point => point.Longitude).IsCoordinate();
        builder.Property(point => point.AccuracyMeters).HasPrecision(8, 2);

        builder.HasReference<DeliveryLocationPoint, DeliveryAttempt>(point => point.DeliveryAttemptId);

        builder.HasCheck("latitude", "latitude BETWEEN -90 AND 90");
        builder.HasCheck("longitude", "longitude BETWEEN -180 AND 180");
        builder.HasCheck("accuracy_meters", "accuracy_meters IS NULL OR accuracy_meters >= 0");

        builder.HasIndex(point => new { point.DeliveryAttemptId, point.RecordedAt }).IsUnique();
    }
}
