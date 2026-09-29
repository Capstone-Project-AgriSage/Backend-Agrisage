using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.GoodsReceipts.Entities;
using AgriSage.Domain.Features.GoodsReceipts.Enums;
using AgriSage.Domain.Features.Products.Entities;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.GoodsReceipts;

public class GoodsReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly StoreProduct _storeProduct;
    private readonly ProductPackaging _bottle;
    private readonly ProductPackaging _box;

    public GoodsReceiptTests()
    {
        var (product, bottle, box) = CreateProductWithPackagings();
        _storeProduct = CreateStoreProduct(product);
        _bottle = bottle;
        _box = box;
    }

    private static GoodsReceipt CreateReceipt() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "GR-0001", Now, StaffId, GoodsReceiptSourceType.Manual);

    [Fact]
    public void New_receipt_is_draft_with_zero_totals()
    {
        var receipt = CreateReceipt();

        Assert.Equal(GoodsReceiptStatus.Draft, receipt.Status);
        Assert.Equal(0m, receipt.SubtotalAmount);
        Assert.Equal(0m, receipt.TotalAmount);
    }

    [Fact]
    public void AddItem_snapshots_conversion_and_calculates_base_values()
    {
        var receipt = CreateReceipt();

        var item = receipt.AddItem(_storeProduct, _box, receivedQuantity: 2, purchaseUnitCost: 120_000m, "LOT-A");

        Assert.Equal(6, item.ConversionToBaseSnapshot);
        Assert.Equal(12, item.BaseQuantity);
        Assert.Equal(20_000m, item.BaseUnitCost);
        Assert.Equal(240_000m, item.LineTotalAmount);
        Assert.Null(item.InventoryLotId);
        Assert.Equal(240_000m, receipt.SubtotalAmount);
        Assert.Equal(240_000m, receipt.TotalAmount);
    }

    [Fact]
    public void Base_unit_cost_is_rounded_away_from_zero_to_6_decimals()
    {
        var receipt = CreateReceipt();

        var boxItem = receipt.AddItem(_storeProduct, _box, receivedQuantity: 1, purchaseUnitCost: 100m);

        Assert.Equal(16.666667m, boxItem.BaseUnitCost);
    }

    [Fact]
    public void Purchase_cost_with_more_than_2_decimals_is_rejected_not_rounded()
    {
        var receipt = CreateReceipt();

        Assert.Throws<DomainException>(() => receipt.AddItem(_storeProduct, _bottle, 5, 0.005m));
        Assert.Empty(receipt.Items);
    }

    [Fact]
    public void Item_cannot_be_soft_deleted_directly_bypassing_the_receipt()
    {
        var receipt = CreateReceipt();
        var item = receipt.AddItem(_storeProduct, _box, 1, 100m);
        receipt.Confirm(StaffId, Now);

        Assert.Throws<DomainException>(() => item.MarkDeleted(StaffId, Now));
        Assert.False(item.IsDeleted);
        Assert.Equal(100m, receipt.TotalAmount);
    }

    [Fact]
    public void Totals_follow_item_updates_and_removals()
    {
        var receipt = CreateReceipt();
        var boxItem = receipt.AddItem(_storeProduct, _box, 2, 120_000m);
        var bottleItem = receipt.AddItem(_storeProduct, _bottle, 5, 25_000m);
        Assert.Equal(365_000m, receipt.TotalAmount);

        receipt.UpdateItem(boxItem.Id, 3, 120_000m, "LOT-A", null, new DateOnly(2027, 12, 31), null);
        Assert.Equal(18, boxItem.BaseQuantity);
        Assert.Equal(485_000m, receipt.TotalAmount);

        receipt.RemoveItem(bottleItem.Id, StaffId, Now);
        Assert.True(bottleItem.IsDeleted);
        Assert.Equal(360_000m, receipt.SubtotalAmount);
        Assert.Equal(360_000m, receipt.TotalAmount);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, -0.01)]
    public void AddItem_rejects_invalid_quantity_or_cost(long receivedQuantity, double purchaseUnitCost)
    {
        var receipt = CreateReceipt();

        Assert.Throws<DomainException>(() =>
            receipt.AddItem(_storeProduct, _box, receivedQuantity, (decimal)purchaseUnitCost));
        Assert.Empty(receipt.Items);
    }

    [Fact]
    public void AddItem_rejects_packaging_of_another_product()
    {
        var receipt = CreateReceipt();
        var (_, _, otherBox) = CreateProductWithPackagings("SKU-OTHER");

        Assert.Throws<DomainException>(() => receipt.AddItem(_storeProduct, otherBox, 1, 100m));
    }

    [Fact]
    public void Confirm_requires_at_least_one_item()
    {
        var receipt = CreateReceipt();
        var item = receipt.AddItem(_storeProduct, _box, 1, 100m);
        receipt.RemoveItem(item.Id, StaffId, Now);

        Assert.Throws<DomainException>(() => receipt.Confirm(StaffId, Now));
        Assert.Equal(GoodsReceiptStatus.Draft, receipt.Status);
    }

    [Fact]
    public void Confirm_records_actor_and_time()
    {
        var receipt = CreateReceipt();
        receipt.AddItem(_storeProduct, _box, 1, 100m);

        receipt.Confirm(StaffId, Now);

        Assert.Equal(GoodsReceiptStatus.Confirmed, receipt.Status);
        Assert.Equal(StaffId, receipt.ConfirmedBy);
        Assert.Equal(Now, receipt.ConfirmedAt);
    }

    [Fact]
    public void Confirmed_receipt_is_immutable()
    {
        var receipt = CreateReceipt();
        var item = receipt.AddItem(_storeProduct, _box, 1, 100m);
        receipt.Confirm(StaffId, Now);

        Assert.Throws<DomainException>(() => receipt.AddItem(_storeProduct, _box, 1, 100m));
        Assert.Throws<DomainException>(() => receipt.UpdateItem(item.Id, 5, 100m, null, null, null, null));
        Assert.Throws<DomainException>(() => receipt.RemoveItem(item.Id, StaffId, Now));
        Assert.Throws<DomainException>(() => receipt.UpdateHeader(Guid.NewGuid(), "INV-1", null, Now, StaffId, null));
        Assert.Throws<DomainException>(() => receipt.Confirm(StaffId, Now));
        Assert.Throws<DomainException>(() => receipt.Cancel(StaffId, Now, "Wrong supplier"));
        Assert.Throws<DomainException>(() => receipt.MarkDeleted(StaffId, Now));

        Assert.Equal(1, item.ReceivedQuantity);
        Assert.Equal(100m, receipt.TotalAmount);
        Assert.False(receipt.IsDeleted);
    }

    [Fact]
    public void Draft_receipt_can_be_cancelled_and_then_no_longer_changed()
    {
        var receipt = CreateReceipt();

        receipt.Cancel(StaffId, Now, "Duplicate entry");

        Assert.Equal(GoodsReceiptStatus.Cancelled, receipt.Status);
        Assert.Equal(StaffId, receipt.CancelledBy);
        Assert.Equal("Duplicate entry", receipt.CancelReason);
        Assert.Throws<DomainException>(() => receipt.AddItem(_storeProduct, _box, 1, 100m));
        Assert.Throws<DomainException>(() => receipt.Confirm(StaffId, Now));
    }

    [Fact]
    public void Draft_receipt_can_be_soft_deleted()
    {
        var receipt = CreateReceipt();

        receipt.MarkDeleted(StaffId, Now);

        Assert.True(receipt.IsDeleted);
    }
}
