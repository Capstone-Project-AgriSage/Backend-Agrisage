using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Deliveries.Entities;
using AgriSage.Domain.Features.Deliveries.Enums;
using AgriSage.Domain.Features.Orders.Entities;
using AgriSage.Domain.Features.Orders.Enums;
using static AgriSage.UnitTests.Domain.Features.Orders.OrderTestData;

namespace AgriSage.UnitTests.Domain.Features.Deliveries;

public class DeliveryTests
{
    private const string ProofUrl = "https://storage.example/proof.jpg";

    private static readonly Guid MemberId = Guid.NewGuid();

    private readonly Order _order;
    private readonly OrderItem _orderItem;

    public DeliveryTests()
    {
        (_order, _orderItem) = CreateConfirmedOrderWithItem(FulfillmentType.Delivery);
    }

    private Delivery CreateDelivery() => new(_order, "DL-0001", Address, StaffId);

    // 2 boxes (12 base units) planned and allocated from one Lot, assigned and dispatched.
    private (Delivery Delivery, DeliveryItem Item, DeliveryItemLotAllocation Allocation) CreateDispatchedDelivery()
    {
        var delivery = CreateDelivery();
        var item = delivery.AddItem(_orderItem, BoxQuantity);
        var allocation = delivery.AllocateLot(item.Id, Guid.NewGuid(), BaseQuantity);
        delivery.Assign(MemberId);
        delivery.Dispatch(Now);
        return (delivery, item, allocation);
    }

    private static Dictionary<Guid, long> Quantities(DeliveryItemLotAllocation allocation, long quantity) =>
        new() { [allocation.Id] = quantity };

    [Fact]
    public void Pickup_order_cannot_have_a_delivery()
    {
        var (pickupOrder, _) = CreateConfirmedOrderWithItem(FulfillmentType.Pickup);

        Assert.Throws<DomainException>(() => new Delivery(pickupOrder, "DL-0002", Address, StaffId));
    }

    [Fact]
    public void Planned_quantity_and_allocations_are_bounded()
    {
        var delivery = CreateDelivery();

        Assert.Throws<DomainException>(() => delivery.AddItem(_orderItem, BoxQuantity + 1));

        var item = delivery.AddItem(_orderItem, BoxQuantity);
        Assert.Equal(BaseQuantity, item.PlannedBaseQuantity);

        delivery.AllocateLot(item.Id, Guid.NewGuid(), 8);
        Assert.Throws<DomainException>(() => delivery.AllocateLot(item.Id, Guid.NewGuid(), 5));
    }

    [Fact]
    public void Successful_attempt_delivers_everything()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));

        delivery.CompleteAttempt(attempt.Id, Now.AddHours(2), Quantities(allocation, BaseQuantity), "Nguyen Van A", ProofUrl);

        Assert.Equal(DeliveryAttemptStatus.Success, attempt.Status);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(Now.AddHours(2), delivery.CompletedAt);
        Assert.Equal(DeliveryItemStatus.Delivered, item.Status);
        Assert.Equal(DeliveryLotAllocationStatus.Delivered, allocation.Status);
    }

    [Fact]
    public void Partial_success_then_retry_completes_the_delivery()
    {
        // Example from database design §42: 20 attempted / 18 delivered, then 2 / 2.
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var first = delivery.StartAttempt(MemberId, Now, Quantities(allocation, 12));

        delivery.CompleteAttempt(first.Id, Now, Quantities(allocation, 8), proofImageUrl: ProofUrl);

        Assert.Equal(DeliveryAttemptStatus.PartialSuccess, first.Status);
        Assert.Equal(4, first.Items.Single().FailedBaseQuantity);
        Assert.Equal(DeliveryStatus.PartiallyDelivered, delivery.Status);
        Assert.Equal(DeliveryItemStatus.PartiallyDelivered, item.Status);
        Assert.Equal(4, allocation.UndeliveredQuantity);

        delivery.Dispatch(Now.AddDays(1));
        var second = delivery.StartAttempt(MemberId, Now.AddDays(1), Quantities(allocation, 4));
        delivery.CompleteAttempt(second.Id, Now.AddDays(1), Quantities(allocation, 4), proofImageUrl: ProofUrl);

        Assert.Equal(2, second.AttemptNumber);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(BaseQuantity, item.DeliveredBaseQuantity);
    }

    [Fact]
    public void Failed_attempt_leaves_delivery_retry_pending_without_proof()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));

        delivery.CompleteAttempt(attempt.Id, Now, new Dictionary<Guid, long>(), failureReasonCode: "CUSTOMER_ABSENT");

        Assert.Equal(DeliveryAttemptStatus.Failed, attempt.Status);
        Assert.Equal(DeliveryStatus.RetryPending, delivery.Status);
        Assert.Equal(0, item.DeliveredBaseQuantity);
        Assert.Throws<DomainException>(() => delivery.LinkSaleStockMovement(attempt.Id, Guid.NewGuid()));
    }

    [Fact]
    public void Delivered_goods_require_proof_and_nothing_changes_without_it()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));

        Assert.Throws<DomainException>(() =>
            delivery.CompleteAttempt(attempt.Id, Now, Quantities(allocation, BaseQuantity)));

        Assert.Equal(DeliveryAttemptStatus.InProgress, attempt.Status);
        Assert.Equal(0, attempt.DeliveredBaseQuantity);
        Assert.Equal(0, item.DeliveredBaseQuantity);
        Assert.Equal(DeliveryStatus.OutForDelivery, delivery.Status);
    }

    [Fact]
    public void Only_one_attempt_in_progress_and_attempt_bounded_by_allocation()
    {
        var (delivery, _, allocation) = CreateDispatchedDelivery();

        Assert.Throws<DomainException>(() => delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity + 1)));

        delivery.StartAttempt(MemberId, Now, Quantities(allocation, 6));
        Assert.Throws<DomainException>(() => delivery.StartAttempt(MemberId, Now, Quantities(allocation, 6)));
    }

    [Fact]
    public void Changing_the_lot_before_dispatch_releases_and_reallocates()
    {
        var delivery = CreateDelivery();
        var item = delivery.AddItem(_orderItem, BoxQuantity);
        var original = delivery.AllocateLot(item.Id, Guid.NewGuid(), BaseQuantity);

        delivery.ReleaseAllocation(original.Id);
        var replacement = delivery.AllocateLot(item.Id, Guid.NewGuid(), BaseQuantity);

        Assert.Equal(DeliveryLotAllocationStatus.Released, original.Status);
        Assert.Equal(BaseQuantity, original.ReleasedBaseQuantity);
        Assert.Equal(DeliveryLotAllocationStatus.Allocated, replacement.Status);
        Assert.Equal(2, item.LotAllocations.Count);
    }

    [Fact]
    public void Allocations_cannot_change_while_out_for_delivery()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();

        Assert.Throws<DomainException>(() => delivery.ReleaseAllocation(allocation.Id));
        Assert.Throws<DomainException>(() => delivery.AllocateLot(item.Id, Guid.NewGuid(), 1));
    }

    [Fact]
    public void Cancelling_after_partial_delivery_ends_delivered_with_the_rest_cancelled()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));
        delivery.CompleteAttempt(attempt.Id, Now, Quantities(allocation, 8), proofImageUrl: ProofUrl);

        delivery.Cancel(StaffId, Now, "Customer refused the remaining bags");

        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(DeliveryItemStatus.PartiallyCancelled, item.Status);
        Assert.Equal(4, item.CancelledBaseQuantity);
        Assert.Equal(DeliveryLotAllocationStatus.Delivered, allocation.Status);
        Assert.Equal(4, allocation.ReleasedBaseQuantity);
    }

    [Fact]
    public void Cancelling_before_any_delivery_cancels_everything()
    {
        var (delivery, item, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));

        Assert.Throws<DomainException>(() => delivery.Cancel(StaffId, Now));

        delivery.CancelAttempt(attempt.Id);
        delivery.Cancel(StaffId, Now, "Order cancelled");

        Assert.Equal(DeliveryStatus.Cancelled, delivery.Status);
        Assert.Equal(DeliveryItemStatus.Cancelled, item.Status);
        Assert.Equal(DeliveryLotAllocationStatus.Cancelled, allocation.Status);
        Assert.Equal(DeliveryAttemptStatus.Cancelled, attempt.Status);
    }

    [Fact]
    public void Successful_attempt_links_its_sale_stock_movement_once()
    {
        var (delivery, _, allocation) = CreateDispatchedDelivery();
        var attempt = delivery.StartAttempt(MemberId, Now, Quantities(allocation, BaseQuantity));
        delivery.CompleteAttempt(attempt.Id, Now, Quantities(allocation, BaseQuantity), proofImageUrl: ProofUrl);
        var movementId = Guid.NewGuid();

        delivery.LinkSaleStockMovement(attempt.Id, movementId);

        Assert.Equal(movementId, attempt.SaleStockMovementId);
        Assert.Throws<DomainException>(() => delivery.LinkSaleStockMovement(attempt.Id, Guid.NewGuid()));
    }

    [Fact]
    public void Dispatch_requires_an_assigned_member()
    {
        var delivery = CreateDelivery();
        var item = delivery.AddItem(_orderItem, BoxQuantity);
        delivery.AllocateLot(item.Id, Guid.NewGuid(), BaseQuantity);

        Assert.Throws<DomainException>(() => delivery.Dispatch(Now));
    }

    [Fact]
    public void Incident_is_resolved_once_and_affected_quantity_must_be_positive()
    {
        Assert.Throws<DomainException>(() => new DeliveryIncident(
            Guid.NewGuid(), DeliveryIncidentType.Damaged, "Bag torn", StaffId, Now, affectedBaseQuantity: 0));

        var incident = new DeliveryIncident(
            Guid.NewGuid(), DeliveryIncidentType.Damaged, "Bag torn", StaffId, Now,
            deliveryItemLotAllocationId: Guid.NewGuid(), affectedBaseQuantity: 2);

        incident.Resolve(DeliveryIncidentResolutionType.ReplaceGoods, StaffId, Now, "Replacement on next delivery");

        Assert.Equal(DeliveryIncidentStatus.Resolved, incident.Status);
        Assert.Throws<DomainException>(() => incident.Resolve(DeliveryIncidentResolutionType.NoAction, StaffId, Now));
    }
}
