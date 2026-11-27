using AgriSage.Application.Features.Deliveries;

namespace AgriSage.UnitTests.Application.Deliveries;

// Request validation of live delivery location (FLOW_2 §9a), before any database access.
public class RecordLocationsValidatorTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 3, 10, 20, TimeSpan.Zero);

    private static LocationPointRequest Point(decimal latitude = 10.04m, decimal longitude = 105.74m, decimal? accuracy = 12m,
        DateTimeOffset? recordedAt = null) => new(latitude, longitude, accuracy, recordedAt ?? At);

    private static bool Valid(params LocationPointRequest[] points) =>
        new RecordLocationsRequestValidator().Validate(new RecordLocationsRequest(points)).IsValid;

    [Fact]
    public void A_batch_holds_1_to_100_points()
    {
        Assert.True(Valid(Point()));
        Assert.True(Valid(Enumerable.Range(0, 100).Select(i => Point(recordedAt: At.AddSeconds(i))).ToArray()));
        Assert.False(Valid());
        Assert.False(Valid(Enumerable.Range(0, 101).Select(i => Point(recordedAt: At.AddSeconds(i))).ToArray()));
        Assert.False(new RecordLocationsRequestValidator().Validate(new RecordLocationsRequest(null!)).IsValid);
    }

    [Fact]
    public void Each_point_has_valid_coordinates_an_optional_accuracy_and_a_time()
    {
        Assert.True(Valid(Point(accuracy: null)));
        Assert.True(Valid(Point(-90m, -180m, 0m), Point(90m, 180m, 10000m, At.AddSeconds(1))));
        Assert.False(Valid(Point(latitude: 90.1m)));
        Assert.False(Valid(Point(longitude: -180.1m)));
        Assert.False(Valid(Point(accuracy: -1m)));
        Assert.False(Valid(Point(accuracy: 10000.5m)));
        Assert.False(Valid(Point(recordedAt: default(DateTimeOffset))));
    }
}
