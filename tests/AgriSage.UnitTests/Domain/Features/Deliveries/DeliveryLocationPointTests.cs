using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Domain.Features.Deliveries;

// F2.8 (DATABASE_DESIGN §35.26): a driver position is recorded only while its attempt runs, inside valid coordinates.
public class DeliveryLocationPointTests
{
    private const string ProofUrl = "https://storage.example/proof.jpg";

    private static readonly Guid MemberId = Guid.NewGuid();

    private static (Delivery Delivery, DeliveryAttempt Attempt, Guid AllocationId) RunningAttempt()
    {
        var (order, orderItem) = CreateConfirmedOrderWithItem(FulfillmentType.Delivery);
        var delivery = new Delivery(order, "DL-0001", Address, StaffId);
        var item = delivery.AddItem(orderItem, BoxQuantity);
        var allocation = delivery.AllocateLot(item.Id, Guid.NewGuid(), BaseQuantity);
        delivery.Assign(MemberId);
        delivery.Dispatch(Now);
        var attempt = delivery.StartAttempt(MemberId, Now, new Dictionary<Guid, long> { [allocation.Id] = BaseQuantity });
        return (delivery, attempt, allocation.Id);
    }

    [Fact]
    public void A_point_of_a_running_attempt_keeps_7_decimals_and_a_utc_millisecond_time()
    {
        var (_, attempt, _) = RunningAttempt();
        var takenAt = new DateTimeOffset(2026, 9, 1, 15, 10, 20, TimeSpan.FromHours(7)).AddTicks(1_234_567);

        var point = DeliveryLocationPoint.Record(attempt, 10.045210149m, 105.746920151m, 12.345m, takenAt);

        Assert.Equal(attempt.Id, point.DeliveryAttemptId);
        Assert.Equal(10.0452101m, point.Latitude);
        Assert.Equal(105.7469202m, point.Longitude);
        Assert.Equal(12.35m, point.AccuracyMeters);
        Assert.Equal(TimeSpan.Zero, point.RecordedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 8, 10, 20, 123, TimeSpan.Zero), point.RecordedAt);
    }

    [Fact]
    public void Accuracy_is_optional()
    {
        var (_, attempt, _) = RunningAttempt();

        Assert.Null(DeliveryLocationPoint.Record(attempt, 10m, 105m, null, Now).AccuracyMeters);
    }

    [Fact]
    public void No_point_is_recorded_once_the_attempt_has_ended()
    {
        var (delivery, attempt, allocationId) = RunningAttempt();
        delivery.CompleteAttempt(attempt.Id, Now.AddHours(1), new Dictionary<Guid, long> { [allocationId] = BaseQuantity },
            "Nguyen Van A", ProofUrl);

        Assert.Throws<DomainException>(() => DeliveryLocationPoint.Record(attempt, 10m, 105m, 5m, Now.AddHours(1)));
    }

    [Theory]
    [InlineData(-90.0000001, 105)]
    [InlineData(90.0000001, 105)]
    [InlineData(10, -180.0000001)]
    [InlineData(10, 180.0000001)]
    public void Coordinates_must_be_on_the_earth(double latitude, double longitude)
    {
        var (_, attempt, _) = RunningAttempt();

        Assert.Throws<DomainException>(() => DeliveryLocationPoint.Record(attempt, (decimal)latitude, (decimal)longitude, null, Now));
    }

    [Fact]
    public void The_limits_themselves_are_valid_coordinates()
    {
        var (_, attempt, _) = RunningAttempt();

        Assert.Equal(-90m, DeliveryLocationPoint.Record(attempt, -90m, 180m, 0m, Now).Latitude);
        Assert.Equal(-180m, DeliveryLocationPoint.Record(attempt, 90m, -180m, DeliveryLocationPoint.MaxAccuracyMeters, Now).Longitude);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10000.01)]
    public void Accuracy_must_be_a_real_distance(double accuracy)
    {
        var (_, attempt, _) = RunningAttempt();

        Assert.Throws<DomainException>(() => DeliveryLocationPoint.Record(attempt, 10m, 105m, (decimal)accuracy, Now));
    }

    [Fact]
    public void Times_are_compared_in_utc_whole_milliseconds()
    {
        var local = new DateTimeOffset(2026, 9, 1, 15, 0, 0, 999, TimeSpan.FromHours(7)).AddTicks(9_999);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 8, 0, 0, 999, TimeSpan.Zero), DeliveryLocationPoint.NormalizeTime(local));
    }
}
