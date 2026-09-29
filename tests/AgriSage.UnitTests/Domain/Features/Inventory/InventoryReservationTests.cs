using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

public class InventoryReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static (InventoryReservation Reservation, InventoryReservationItem Item) CreateReservation(long quantity = 10)
    {
        var reservation = new InventoryReservation(Guid.NewGuid(), Guid.NewGuid(), StaffId, Now);
        var item = reservation.AddItem(Guid.NewGuid(), Guid.NewGuid(), quantity);
        return (reservation, item);
    }

    [Fact]
    public void Consuming_moves_from_active_to_partially_consumed_to_consumed()
    {
        var (reservation, item) = CreateReservation();
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);

        reservation.Consume(item.Id, 4);
        Assert.Equal(InventoryReservationStatus.PartiallyConsumed, reservation.Status);

        reservation.Consume(item.Id, 6);
        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);
        Assert.Equal(0, reservation.RemainingQuantity);
    }

    [Fact]
    public void Releasing_the_rest_after_partial_consumption_ends_consumed()
    {
        var (reservation, item) = CreateReservation();
        reservation.Consume(item.Id, 4);

        reservation.ReleaseRemaining(StaffId, Now, "Remaining fulfillment no longer required");

        Assert.Equal(InventoryReservationStatus.Consumed, reservation.Status);
        Assert.Equal(6, item.BaseQuantityReleased);
        Assert.Equal(StaffId, reservation.ReleasedBy);
    }

    [Fact]
    public void Releasing_everything_without_consumption_ends_released()
    {
        var (reservation, _) = CreateReservation();

        reservation.ReleaseRemaining(StaffId, Now);

        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
    }

    [Fact]
    public void Partial_release_keeps_the_current_status()
    {
        var (reservation, item) = CreateReservation();

        reservation.Release(item.Id, 3, StaffId, Now);

        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(7, reservation.RemainingQuantity);
    }

    [Fact]
    public void Cancel_is_only_possible_before_any_consumption()
    {
        var (cancellable, _) = CreateReservation();
        cancellable.Cancel(StaffId, Now, "Order cancelled");
        Assert.Equal(InventoryReservationStatus.Cancelled, cancellable.Status);
        Assert.Equal(0, cancellable.RemainingQuantity);

        var (consumed, item) = CreateReservation();
        consumed.Consume(item.Id, 1);
        Assert.Throws<DomainException>(() => consumed.Cancel(StaffId, Now));
    }

    [Fact]
    public void Consumed_plus_released_cannot_exceed_reserved()
    {
        var (reservation, item) = CreateReservation();
        reservation.Consume(item.Id, 6);
        reservation.Release(item.Id, 3, StaffId, Now);

        Assert.Throws<DomainException>(() => reservation.Consume(item.Id, 2));
        Assert.Equal(1, item.RemainingQuantity);
    }

    [Fact]
    public void Closed_reservation_cannot_change_or_be_deleted()
    {
        var (reservation, item) = CreateReservation();
        reservation.Consume(item.Id, 10);

        Assert.Throws<DomainException>(() => reservation.AddItem(Guid.NewGuid(), Guid.NewGuid(), 1));
        Assert.Throws<DomainException>(() => reservation.MarkDeleted(StaffId, Now));
    }

    [Fact]
    public void Same_order_item_and_lot_cannot_be_reserved_on_two_lines()
    {
        var reservation = new InventoryReservation(Guid.NewGuid(), Guid.NewGuid(), StaffId, Now);
        var orderItemId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        reservation.AddItem(orderItemId, lotId, 5);

        Assert.Throws<DomainException>(() => reservation.AddItem(orderItemId, lotId, 5));
    }
}
