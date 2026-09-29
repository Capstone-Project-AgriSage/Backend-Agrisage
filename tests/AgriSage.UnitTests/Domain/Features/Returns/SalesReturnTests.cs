using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Returns.Entities;
using AgriSage.Domain.Features.Returns.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;
using static AgriSage.UnitTests.Domain.Features.Products.CatalogTestData;

namespace AgriSage.UnitTests.Domain.Features.Returns;

public class SalesReturnTests
{
    private readonly Order _order;
    private readonly OrderItem _orderItem;

    // Pickup order of 2 boxes (12 base units) at 120,000 per box, fully handed over.
    public SalesReturnTests()
    {
        (_order, _orderItem) = CreateConfirmedOrderWithItem();
        _order.RecordFulfillment(_orderItem.Id, BaseQuantity, StaffId, Now);
    }

    private SalesReturn CreateReturn() => new(_order, "SR-0001", StaffId, Now);

    private SalesReturnItem AddLine(SalesReturn salesReturn, long quantity, long alreadyReturned = 0) =>
        salesReturn.AddItem(
            _order,
            _orderItem,
            quantity,
            alreadyReturned,
            inventoryLotId: Guid.NewGuid(),
            reasonCode: "QUALITY_ISSUE",
            originalStockMovementItemId: Guid.NewGuid(),
            originalCogsUnitCost: 15_000m);

    // Approved, received and every line inspected with the given condition.
    private (SalesReturn Return, SalesReturnItem Line) CreateInspectedReturn(
        ReturnConditionStatus condition,
        long quantity,
        decimal debtAdjustment)
    {
        var salesReturn = CreateReturn();
        var line = AddLine(salesReturn, quantity);
        salesReturn.Approve(StaffId, Now);
        salesReturn.MarkReceived(StaffId, Now);
        salesReturn.InspectItem(line.Id, condition);
        salesReturn.CompleteInspection(StaffId, Now, debtAdjustment);
        return (salesReturn, line);
    }

    [Fact]
    public void Line_snapshots_the_original_price_and_values_the_returned_base_units()
    {
        var salesReturn = CreateReturn();

        var line = AddLine(salesReturn, 3);

        Assert.Equal(BoxPrice, line.SellingUnitPriceSnapshot);
        Assert.Equal(6, line.ConversionToBaseSnapshot);
        Assert.Equal(60_000m, line.ReturnValue);
        Assert.Equal(45_000m, line.ReturnInventoryCostValue);
        Assert.Equal(60_000m, salesReturn.TotalReturnAmount);
        Assert.Equal(_order.FarmerProfileId, salesReturn.FarmerProfileId);
        Assert.Equal(ReturnConditionStatus.PendingInspection, line.ConditionStatus);
        Assert.Equal(InventoryDisposition.None, line.InventoryDisposition);
    }

    [Fact]
    public void Return_value_multiplies_before_dividing_and_rounds_away_from_zero()
    {
        var order = CreateOrder();
        var (product, _, box) = CreateProductWithPackagings();
        var item = order.AddItem(CreateStoreProduct(product), box, product.Sku, product.Name, "Box of 6", 2, 0.03m);
        order.Confirm(StaffId, Now);
        order.RecordFulfillment(item.Id, 12, StaffId, Now);
        var salesReturn = new SalesReturn(order, "SR-0002", StaffId, Now);

        // 1 × 0.03 ÷ 6 = 0.005 → 0.01 (banker's rounding would give 0.00).
        var single = salesReturn.AddItem(order, item, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid());
        // A whole packaging returns exactly its price.
        var box6 = salesReturn.AddItem(order, item, 6, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid());

        Assert.Equal(0.01m, single.ReturnValue);
        Assert.Equal(0.03m, box6.ReturnValue);
    }

    [Fact]
    public void Returned_quantity_cannot_exceed_fulfilled_minus_already_returned()
    {
        var salesReturn = CreateReturn();

        Assert.Throws<DomainException>(() => AddLine(salesReturn, 8, alreadyReturned: 5));

        AddLine(salesReturn, 4, alreadyReturned: 5);
        AddLine(salesReturn, 3, alreadyReturned: 5);
        Assert.Throws<DomainException>(() => AddLine(salesReturn, 1, alreadyReturned: 5));
    }

    [Fact]
    public void Only_fulfilled_goods_of_the_same_order_can_be_returned()
    {
        var (unfulfilledOrder, unfulfilledItem) = CreateConfirmedOrderWithItem();
        var unfulfilledReturn = new SalesReturn(unfulfilledOrder, "SR-0003", StaffId, Now);
        Assert.Throws<DomainException>(() => unfulfilledReturn.AddItem(
            unfulfilledOrder, unfulfilledItem, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid()));

        var salesReturn = CreateReturn();
        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            _order, unfulfilledItem, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid()));
        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            unfulfilledOrder, unfulfilledItem, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid()));
    }

    [Fact]
    public void Pickup_line_references_exactly_its_original_stock_movement_item()
    {
        var salesReturn = CreateReturn();

        Assert.Throws<DomainException>(() => salesReturn.AddItem(_order, _orderItem, 1, 0, Guid.NewGuid(), "OTHER"));
        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            _order, _orderItem, 1, 0, Guid.NewGuid(), "OTHER", deliveryItemLotAllocationId: Guid.NewGuid()));
        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            _order, _orderItem, 1, 0, Guid.NewGuid(), "OTHER",
            deliveryItemLotAllocationId: Guid.NewGuid(), originalStockMovementItemId: Guid.NewGuid()));

        var line = salesReturn.AddItem(
            _order, _orderItem, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid());
        Assert.Null(line.DeliveryItemLotAllocationId);
    }

    [Fact]
    public void Delivery_line_references_exactly_its_lot_allocation()
    {
        var (order, item) = CreateConfirmedOrderWithItem(AgriSage.Domain.Features.Orders.Enums.FulfillmentType.Delivery);
        order.RecordFulfillment(item.Id, BaseQuantity, StaffId, Now);
        var salesReturn = new SalesReturn(order, "SR-0004", StaffId, Now);

        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            order, item, 1, 0, Guid.NewGuid(), "OTHER", originalStockMovementItemId: Guid.NewGuid()));
        Assert.Throws<DomainException>(() => salesReturn.AddItem(
            order, item, 1, 0, Guid.NewGuid(), "OTHER",
            deliveryItemLotAllocationId: Guid.NewGuid(), originalStockMovementItemId: Guid.NewGuid()));

        var line = salesReturn.AddItem(
            order, item, 1, 0, Guid.NewGuid(), "OTHER", deliveryItemLotAllocationId: Guid.NewGuid());
        Assert.Null(line.OriginalStockMovementItemId);
    }

    [Fact]
    public void Lines_are_locked_after_approval_and_approval_needs_lines()
    {
        var salesReturn = CreateReturn();
        Assert.Throws<DomainException>(() => salesReturn.Approve(StaffId, Now));

        var line = AddLine(salesReturn, 2);
        salesReturn.Approve(StaffId, Now);

        Assert.Equal(SalesReturnStatus.Approved, salesReturn.Status);
        Assert.Throws<DomainException>(() => AddLine(salesReturn, 1));
        Assert.Throws<DomainException>(() => salesReturn.RemoveItem(line.Id, StaffId, Now));
        Assert.Throws<DomainException>(() => salesReturn.MarkDeleted(StaffId, Now));
    }

    [Fact]
    public void Reject_changes_status_only_and_cancel_is_not_possible_after_receipt()
    {
        var rejected = CreateReturn();
        AddLine(rejected, 1);
        rejected.Reject();
        Assert.Equal(SalesReturnStatus.Rejected, rejected.Status);
        Assert.Null(rejected.CancelledAt);

        var received = CreateReturn();
        AddLine(received, 1);
        received.Approve(StaffId, Now);
        received.MarkReceived(StaffId, Now);
        Assert.Throws<DomainException>(() => received.Cancel(StaffId, Now));
    }

    [Theory]
    [InlineData(ReturnConditionStatus.Resellable, InventoryDisposition.Restock)]
    [InlineData(ReturnConditionStatus.Damaged, InventoryDisposition.WriteOff)]
    [InlineData(ReturnConditionStatus.Expired, InventoryDisposition.WriteOff)]
    [InlineData(ReturnConditionStatus.Unusable, InventoryDisposition.WriteOff)]
    public void Inspection_condition_fixes_the_inventory_disposition(
        ReturnConditionStatus condition,
        InventoryDisposition expected)
    {
        var (_, line) = CreateInspectedReturn(condition, 3, debtAdjustment: 0);

        Assert.Equal(expected, line.InventoryDisposition);
    }

    [Fact]
    public void Inspection_must_cover_every_line()
    {
        var salesReturn = CreateReturn();
        var line = AddLine(salesReturn, 3);
        AddLine(salesReturn, 2);
        salesReturn.Approve(StaffId, Now);
        salesReturn.MarkReceived(StaffId, Now);

        Assert.Throws<DomainException>(() => salesReturn.InspectItem(line.Id, ReturnConditionStatus.PendingInspection));
        salesReturn.InspectItem(line.Id, ReturnConditionStatus.Resellable);

        Assert.Throws<DomainException>(() => salesReturn.CompleteInspection(StaffId, Now, 0));
    }

    [Fact]
    public void Settlement_reduces_debt_first_and_refunds_the_rest()
    {
        // Database design §XXII: 60,000 returned while 40,000 is still unpaid → 40,000 debt reduction, 20,000 refund.
        var (salesReturn, _) = CreateInspectedReturn(ReturnConditionStatus.Damaged, 3, debtAdjustment: 40_000m);

        Assert.Equal(SalesReturnStatus.Inspected, salesReturn.Status);
        Assert.Equal(40_000m, salesReturn.TotalDebtAdjustment);
        Assert.Equal(20_000m, salesReturn.TotalRefundAmount);
    }

    [Fact]
    public void Debt_adjustment_cannot_exceed_the_return_value()
    {
        var salesReturn = CreateReturn();
        var line = AddLine(salesReturn, 3);
        salesReturn.Approve(StaffId, Now);
        salesReturn.MarkReceived(StaffId, Now);
        salesReturn.InspectItem(line.Id, ReturnConditionStatus.Damaged);

        Assert.Throws<DomainException>(() => salesReturn.CompleteInspection(StaffId, Now, 60_000.01m));
    }

    [Fact]
    public void Refunds_cannot_exceed_the_refund_total()
    {
        var (salesReturn, _) = CreateInspectedReturn(ReturnConditionStatus.Damaged, 3, debtAdjustment: 40_000m);

        var refund = salesReturn.RequestRefund("RF-0001", RefundMethod.Cash, 20_000m, StaffId, Now);
        Assert.Throws<DomainException>(() => salesReturn.RequestRefund("RF-0002", RefundMethod.Cash, 1m, StaffId, Now));

        salesReturn.CancelRefund(refund.Id, StaffId, Now, "Customer prefers bank transfer");
        var retry = salesReturn.RequestRefund("RF-0003", RefundMethod.BankTransfer, 20_000m, StaffId, Now);
        Assert.Equal(RefundStatus.Pending, retry.Status);
    }

    [Fact]
    public void Refund_states_are_terminal()
    {
        var (salesReturn, _) = CreateInspectedReturn(ReturnConditionStatus.Damaged, 3, debtAdjustment: 0);
        var failed = salesReturn.RequestRefund("RF-0001", RefundMethod.BankTransfer, 10_000m, StaffId, Now);

        salesReturn.FailRefund(failed.Id);

        Assert.Equal(RefundStatus.Failed, failed.Status);
        Assert.Throws<DomainException>(() => salesReturn.CompleteRefund(failed.Id, StaffId, Now));
        Assert.Throws<DomainException>(() => salesReturn.CancelRefund(failed.Id, StaffId, Now));
    }

    [Fact]
    public void Resolution_steps_lead_to_partially_resolved_then_completed()
    {
        var (salesReturn, line) = CreateInspectedReturn(ReturnConditionStatus.Resellable, 3, debtAdjustment: 40_000m);
        var refund = salesReturn.RequestRefund("RF-0001", RefundMethod.Cash, 20_000m, StaffId, Now);

        salesReturn.LinkDebtAdjustmentTransaction(line.Id, Guid.NewGuid());
        Assert.Equal(SalesReturnStatus.PartiallyResolved, salesReturn.Status);

        Assert.Throws<DomainException>(() => salesReturn.Complete(Now));

        salesReturn.LinkReturnStockMovement(line.Id, Guid.NewGuid());
        Assert.Throws<DomainException>(() => salesReturn.Complete(Now));

        salesReturn.CompleteRefund(refund.Id, StaffId, Now, "Cash handed over");
        salesReturn.Complete(Now.AddHours(1));

        Assert.Equal(SalesReturnStatus.Completed, salesReturn.Status);
        Assert.Equal(Now.AddHours(1), salesReturn.CompletedAt);
    }

    [Fact]
    public void Only_restock_lines_get_return_in_and_debt_link_needs_a_debt_adjustment()
    {
        var (salesReturn, line) = CreateInspectedReturn(ReturnConditionStatus.Damaged, 3, debtAdjustment: 0);

        Assert.Throws<DomainException>(() => salesReturn.LinkReturnStockMovement(line.Id, Guid.NewGuid()));
        Assert.Throws<DomainException>(() => salesReturn.LinkDebtAdjustmentTransaction(line.Id, Guid.NewGuid()));
    }

    [Fact]
    public void Lines_and_refunds_cannot_be_deleted_directly()
    {
        var (salesReturn, line) = CreateInspectedReturn(ReturnConditionStatus.Damaged, 3, debtAdjustment: 0);
        var refund = salesReturn.RequestRefund("RF-0001", RefundMethod.Cash, 10_000m, StaffId, Now);

        Assert.Throws<DomainException>(() => line.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => refund.MarkDeleted(StaffId, Now));
    }
}
