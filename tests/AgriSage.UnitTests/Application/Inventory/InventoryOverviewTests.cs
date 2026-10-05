using AgriSage.Application.Common;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Application.Inventory;

public class InventoryOverviewTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    [Fact]
    public void Expiring_a_lot_is_idempotent_and_preserves_its_stock_cost_and_reservation()
    {
        var lot = new InventoryLot(Guid.NewGuid(), "L1", expiryDate: Today.AddDays(-1));
        lot.ReceiveStock(100, 12.345678m);
        lot.Reserve(20, Today.AddDays(-1));

        Assert.True(lot.ExpireIfDue(Today));
        Assert.Equal(InventoryLotStatus.Expired, lot.Status);
        Assert.False(lot.ExpireIfDue(Today));
        Assert.Equal(100, lot.Balance.QuantityOnHand);
        Assert.Equal(20, lot.Balance.QuantityReserved);
        Assert.Equal(1234.5678m, lot.Balance.TotalCostValue);
        Assert.False(lot.IsEligibleForSale(Today));
    }

    [Theory]
    [InlineData(InventoryLotStatus.Blocked)]
    [InlineData(InventoryLotStatus.Quarantined)]
    [InlineData(InventoryLotStatus.Expired)]
    [InlineData(InventoryLotStatus.Depleted)]
    public void Expiration_does_not_override_non_active_statuses(InventoryLotStatus status)
    {
        var lot = new InventoryLot(Guid.NewGuid(), expiryDate: Today.AddDays(-1));
        lot.ChangeStatus(status);

        Assert.False(lot.ExpireIfDue(Today));
        Assert.Equal(status, lot.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void No_expiry_today_and_future_lots_remain_active(int? days)
    {
        var lot = new InventoryLot(Guid.NewGuid(), expiryDate: days is { } d ? Today.AddDays(d) : null);

        Assert.False(lot.ExpireIfDue(Today));
        Assert.Equal(InventoryLotStatus.Active, lot.Status);
    }

    [Fact]
    public void Expiration_changes_at_Vietnam_midnight_instead_of_UTC_midnight()
    {
        var lot = new InventoryLot(Guid.NewGuid(), expiryDate: Today.AddDays(-1));
        var midnight = new DateTimeOffset(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

        Assert.False(lot.ExpireIfDue(BusinessCalendar.Today(midnight.AddTicks(-1))));
        Assert.True(lot.ExpireIfDue(BusinessCalendar.Today(midnight)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("EXPIRING")]
    [InlineData("expired")]
    [InlineData(" low_stock ")]
    public void Alert_types_accept_contract_values_case_insensitively(string? type) =>
        Assert.True(new InventoryAlertsRequestValidator().Validate(new InventoryAlertsRequest() { Type = type }).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Alert_window_rejects_values_outside_contract(int days) =>
        Assert.False(new InventoryAlertsRequestValidator().Validate(new InventoryAlertsRequest() { WithinDays = days }).IsValid);

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Alert_window_accepts_both_boundaries(int days) =>
        Assert.True(new InventoryAlertsRequestValidator().Validate(new InventoryAlertsRequest() { WithinDays = days }).IsValid);

    [Fact]
    public void Invalid_alert_type_pagination_and_long_search_are_rejected()
    {
        Assert.False(new InventoryAlertsRequestValidator().Validate(new InventoryAlertsRequest() { Type = "ACTIVE" }).IsValid);
        Assert.False(new InventoryAlertsRequestValidator().Validate(new InventoryAlertsRequest() { Page = 0 }).IsValid);
        Assert.False(new StockSummaryRequestValidator().Validate(new StockSummaryRequest() { PageSize = 101 }).IsValid);
        Assert.False(new StockSummaryRequestValidator().Validate(new StockSummaryRequest() { Search = new string('x', 101) }).IsValid);
    }
}
