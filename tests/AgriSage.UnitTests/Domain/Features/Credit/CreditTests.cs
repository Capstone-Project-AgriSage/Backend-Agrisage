using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Credit.Entities;
using AgriSage.Domain.Features.Credit.Enums;

namespace AgriSage.UnitTests.Domain.Features.Credit;

public class CreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static CreditReservation CreateReservation(decimal amount = 50_000_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, StaffId, Now);

    private static FarmerCreditProfile CreateProfile(decimal creditLimit = 100_000_000m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), creditLimit, StaffId, Now);

    [Fact]
    public void Reservation_follows_the_partial_delivery_example()
    {
        // Database design §XVI case B: reserve 50M, deliver 20M, then 30M.
        var reservation = CreateReservation();

        reservation.Consume(20_000_000m);
        Assert.Equal(CreditReservationStatus.PartiallyConsumed, reservation.Status);
        Assert.Equal(30_000_000m, reservation.RemainingAmount);

        reservation.Consume(30_000_000m);
        Assert.Equal(CreditReservationStatus.Consumed, reservation.Status);
    }

    [Fact]
    public void Additional_prepayment_releases_part_without_changing_status()
    {
        // Database design §XVI case D: 20M paid before delivery reduces the 50M reservation.
        var reservation = CreateReservation();

        reservation.Release(20_000_000m, StaffId, Now, "Additional upfront payment");

        Assert.Equal(CreditReservationStatus.Active, reservation.Status);
        Assert.Equal(30_000_000m, reservation.RemainingAmount);
    }

    [Fact]
    public void Releasing_the_rest_ends_consumed_if_anything_was_consumed_otherwise_released()
    {
        var partlyUsed = CreateReservation();
        partlyUsed.Consume(10_000_000m);
        partlyUsed.ReleaseRemaining(StaffId, Now);
        Assert.Equal(CreditReservationStatus.Consumed, partlyUsed.Status);

        var unused = CreateReservation();
        unused.ReleaseRemaining(StaffId, Now);
        Assert.Equal(CreditReservationStatus.Released, unused.Status);
    }

    [Fact]
    public void Cancel_is_only_possible_before_any_consumption()
    {
        var cancellable = CreateReservation();
        cancellable.Cancel(StaffId, Now, "Order cancelled");
        Assert.Equal(CreditReservationStatus.Cancelled, cancellable.Status);
        Assert.Equal(50_000_000m, cancellable.AmountReleased);

        var consumed = CreateReservation();
        consumed.Consume(1_000m);
        Assert.Throws<DomainException>(() => consumed.Cancel(StaffId, Now));
    }

    [Fact]
    public void Consumed_plus_released_cannot_exceed_reserved()
    {
        var reservation = CreateReservation();
        reservation.Consume(40_000_000m);

        Assert.Throws<DomainException>(() => reservation.Consume(10_000_001m));
        Assert.Throws<DomainException>(() => reservation.MarkDeleted(StaffId, Now));
    }

    [Fact]
    public void Credit_limit_change_is_recorded_in_history()
    {
        var profile = CreateProfile(50_000_000m);
        var newTierId = Guid.NewGuid();

        var history = profile.ChangeCreditLimit(80_000_000m, newTierId, "Good repayment record", StaffId, Now);

        Assert.Equal(80_000_000m, profile.CreditLimit);
        Assert.Equal(newTierId, profile.CreditTierId);
        Assert.Equal(50_000_000m, history.OldCreditLimit);
        Assert.Equal(80_000_000m, history.NewCreditLimit);
        Assert.Null(history.OldCreditTierId);
        Assert.Single(profile.LimitHistories);
    }

    [Fact]
    public void Credit_limit_change_requires_a_reason_and_an_actual_change()
    {
        var profile = CreateProfile(50_000_000m);

        Assert.Throws<DomainException>(() => profile.ChangeCreditLimit(50_000_000m, null, "No change", StaffId, Now));
        Assert.Throws<DomainException>(() => profile.ChangeCreditLimit(60_000_000m, null, " ", StaffId, Now));
        Assert.Throws<DomainException>(() => profile.ChangeCreditLimit(-1m, null, "Negative", StaffId, Now));
        Assert.Empty(profile.LimitHistories);
    }

    [Fact]
    public void Available_credit_is_limit_minus_receivable_minus_reserved()
    {
        var profile = CreateProfile(100_000_000m);

        Assert.Equal(30_000_000m, profile.CalculateAvailableCredit(40_000_000m, 30_000_000m));

        profile.EnsureCanReserve(30_000_000m, 40_000_000m, 30_000_000m);
        Assert.Throws<DomainException>(() => profile.EnsureCanReserve(30_000_001m, 40_000_000m, 30_000_000m));
    }

    [Fact]
    public void Only_an_active_profile_can_reserve_credit()
    {
        var profile = CreateProfile();
        profile.ChangeStatus(FarmerCreditProfileStatus.Suspended);

        Assert.False(profile.CanUseCredit);
        Assert.Throws<DomainException>(() => profile.EnsureCanReserve(1_000m, 0, 0));
    }
}
