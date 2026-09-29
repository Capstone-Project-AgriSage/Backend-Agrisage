using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

public class InventoryLotTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);

    private static InventoryLot CreateLot(DateOnly? expiryDate = null) =>
        new(Guid.NewGuid(), "LOT-A", expiryDate: expiryDate ?? Today.AddMonths(6));

    private static InventoryLot CreateStockedLot(long quantity, decimal unitCost)
    {
        var lot = CreateLot();
        lot.ReceiveStock(quantity, unitCost);
        return lot;
    }

    [Fact]
    public void Receiving_updates_weighted_average_cost_per_lot()
    {
        // Example from database design §25.
        var lot = CreateStockedLot(100, 100_000m);

        var change = lot.ReceiveStock(100, 110_000m);

        Assert.Equal(100, change.QuantityDelta);
        Assert.Equal(11_000_000m, change.TotalCost);
        Assert.Equal(200, lot.Balance.QuantityOnHand);
        Assert.Equal(21_000_000m, lot.Balance.TotalCostValue);
        Assert.Equal(105_000m, lot.Balance.AverageUnitCost);
    }

    [Fact]
    public void Reservation_cannot_exceed_available_quantity()
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(8, Today);

        Assert.Throws<DomainException>(() => lot.Reserve(3, Today));

        Assert.Equal(8, lot.Balance.QuantityReserved);
        Assert.Equal(2, lot.Balance.AvailableQuantity);
        Assert.Equal(10, lot.Balance.QuantityOnHand);
    }

    [Theory]
    [InlineData(InventoryLotStatus.Quarantined)]
    [InlineData(InventoryLotStatus.Blocked)]
    [InlineData(InventoryLotStatus.Expired)]
    [InlineData(InventoryLotStatus.Depleted)]
    public void Non_active_lot_cannot_be_reserved_or_sold(InventoryLotStatus status)
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(2, Today);
        lot.ChangeStatus(status);

        Assert.Throws<DomainException>(() => lot.Reserve(1, Today));
        Assert.Throws<DomainException>(() => lot.IssueReserved(2, Today));
    }

    [Fact]
    public void Lot_past_its_expiry_date_cannot_be_reserved_but_expiring_today_can()
    {
        var expired = CreateLot(expiryDate: Today.AddDays(-1));
        expired.ReceiveStock(10, 1_000m);
        var expiringToday = CreateLot(expiryDate: Today);
        expiringToday.ReceiveStock(10, 1_000m);

        Assert.Throws<DomainException>(() => expired.Reserve(1, Today));
        expiringToday.Reserve(1, Today);
        Assert.Equal(1, expiringToday.Balance.QuantityReserved);
    }

    [Fact]
    public void Sale_issues_reserved_stock_at_average_cost()
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(4, Today);

        var change = lot.IssueReserved(4, Today);

        Assert.Equal(-4, change.QuantityDelta);
        Assert.Equal(1_000m, change.UnitCost);
        Assert.Equal(4_000m, change.TotalCost);
        Assert.Equal(6, change.QuantityOnHandAfter);
        Assert.Equal(6_000m, change.TotalCostValueAfter);
        Assert.Equal(0, lot.Balance.QuantityReserved);
    }

    [Fact]
    public void Sale_cannot_exceed_reserved_quantity()
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(2, Today);

        Assert.Throws<DomainException>(() => lot.IssueReserved(3, Today));
        Assert.Equal(10, lot.Balance.QuantityOnHand);
    }

    [Fact]
    public void Adjustment_out_cannot_take_reserved_stock()
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(8, Today);

        Assert.Throws<DomainException>(() => lot.IssueUnreserved(3));

        lot.IssueUnreserved(2);
        Assert.Equal(8, lot.Balance.QuantityOnHand);
        Assert.Equal(8, lot.Balance.QuantityReserved);
    }

    [Fact]
    public void Issuance_that_empties_the_lot_takes_the_exact_remaining_cost()
    {
        var lot = CreateLot();
        lot.ReceiveStock(1, 3.333334m);
        lot.ReceiveStock(2, 3.333333m);
        lot.Reserve(3, Today);
        Assert.Equal(10m, lot.Balance.TotalCostValue);
        Assert.Equal(3.333333m, lot.Balance.AverageUnitCost);

        var change = lot.IssueReserved(3, Today);

        // 3 × 3.333333 = 9.999999; the last issuance takes the full 10 so no value is lost.
        Assert.Equal(10m, change.TotalCost);
        Assert.Equal(3.333333m, change.UnitCost);
        Assert.Equal(0, lot.Balance.QuantityOnHand);
        Assert.Equal(0m, lot.Balance.TotalCostValue);
        Assert.Null(lot.Balance.AverageUnitCost);
    }

    [Fact]
    public void Reversing_an_inflow_removes_it_at_its_own_cost()
    {
        var lot = CreateStockedLot(100, 100_000m);
        lot.ReceiveStock(100, 110_000m);

        var change = lot.RemoveStockAtCost(100, 110_000m);

        Assert.Equal(-100, change.QuantityDelta);
        Assert.Equal(100, lot.Balance.QuantityOnHand);
        Assert.Equal(10_000_000m, lot.Balance.TotalCostValue);
    }

    [Fact]
    public void Release_cannot_exceed_reserved_quantity()
    {
        var lot = CreateStockedLot(10, 1_000m);
        lot.Reserve(2, Today);

        Assert.Throws<DomainException>(() => lot.ReleaseReservation(3));

        lot.ReleaseReservation(2);
        Assert.Equal(0, lot.Balance.QuantityReserved);
    }

    [Fact]
    public void Unit_cost_with_more_than_6_decimals_is_rejected()
    {
        var lot = CreateLot();

        Assert.Throws<DomainException>(() => lot.ReceiveStock(1, 1.0000001m));
        Assert.Equal(0, lot.Balance.QuantityOnHand);
    }

    [Fact]
    public void Balance_cannot_be_deleted_directly()
    {
        var lot = CreateLot();

        Assert.Throws<DomainException>(() => lot.Balance.MarkDeleted(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
