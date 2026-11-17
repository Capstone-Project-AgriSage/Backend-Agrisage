using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Products.Entities;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

public class ReplenishmentTests
{
    [Fact]
    public void Worked_example_covers_lead_time_safety_and_purchase_multiple_without_double_subtracting_stock()
    {
        var daily = ReplenishmentMath.DailyDemand(150, 30, 20);
        Assert.Equal(5m, daily);
        var target = ReplenishmentMath.Target(null, 30, daily, 4, 10, 10, 1m, null);
        Assert.Equal(80, target);
        Assert.Equal(60, ReplenishmentMath.PurchaseQuantity(target, 20, 12));
        Assert.Equal(72, ReplenishmentMath.PurchaseQuantity(target, 20, 24));
        Assert.Equal(4m, ReplenishmentMath.DaysRemaining(20, daily));
    }
    [Fact]
    public void Sparse_history_never_invents_dates_or_demand_and_can_use_manual_minimum()
    {
        Assert.Null(ReplenishmentMath.DailyDemand(100, 13, 3));
        Assert.Null(ReplenishmentMath.DailyDemand(100, 90, 2));
        Assert.Null(ReplenishmentMath.DaysRemaining(20, null));
        Assert.Null(ReplenishmentMath.DaysRemaining(20, 0));
        Assert.Null(ReplenishmentMath.Target(null, null, null, 7, 14, 0, 1, null));
        Assert.Equal(50, ReplenishmentMath.Target(null, 50, null, 7, 14, 0, 1, null));
        Assert.Equal(0m, ReplenishmentMath.DailyDemand(-100, 30, 3));
    }
    [Fact]
    public void Seasonal_target_is_a_floor_not_an_additional_order()
    {
        Assert.Equal(150, ReplenishmentMath.Target(100, 30, 5, 4, 10, 10, 1.5m, 130));
        Assert.Equal(180, ReplenishmentMath.Target(100, 30, 5, 4, 10, 10, 1.5m, 180));
        Assert.Equal(0, ReplenishmentMath.PurchaseQuantity(100, 120, 12));
    }
    [Fact]
    public void Rule_rejects_invalid_dates_duplicates_and_negative_targets_before_mutating_items()
    {
        var today = new DateOnly(2026, 11, 2);
        Assert.Throws<DomainException>(() => new SeasonalRule(Guid.NewGuid(), "Season", "RICE", null, today, today.AddDays(-1), 14, 1));
        var rule = new SeasonalRule(Guid.NewGuid(), "Season", "RICE", null, today, today.AddDays(30), 14, 1);
        var product = Guid.NewGuid();
        rule.SetItems([(product, 100L)], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => rule.SetItems([(product, 10L), (product, 20L)], Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal(100, Assert.Single(rule.Items).TargetStockLevelBase);
        rule.SetItems([(product, 120L)], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(120, Assert.Single(rule.Items).TargetStockLevelBase);
    }
    [Fact]
    public void Store_settings_reject_target_below_minimum_without_changing_valid_settings()
    {
        var product = new StoreProduct(Guid.NewGuid(), Guid.NewGuid(), minStockLevelBase: 30);
        product.ConfigureReplenishment(true, 100, 4, 10, 14);
        Assert.Throws<DomainException>(() => product.ConfigureReplenishment(true, 20, 4, 10, 14));
        Assert.Equal(100, product.TargetStockLevelBase);
    }
}
