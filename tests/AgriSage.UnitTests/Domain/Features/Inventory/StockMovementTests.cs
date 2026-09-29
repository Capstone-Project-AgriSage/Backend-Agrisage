using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

public class StockMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid StaffId = Guid.NewGuid();

    private static readonly LotBalanceChange Inflow = new(10, 1_000m, 10_000m, 10, 10_000m);
    private static readonly LotBalanceChange Outflow = new(-4, 1_000m, 4_000m, 6, 6_000m);

    private static StockMovement CreateMovement(StockMovementType type, Guid? reversalOf = null) =>
        new(Guid.NewGuid(), "SM-0001", type, Now, StaffId, reversalOfMovementId: reversalOf);

    [Fact]
    public void Item_copies_the_lot_balance_change_snapshot()
    {
        var movement = CreateMovement(StockMovementType.StockIn);

        var item = movement.AddItem(Guid.NewGuid(), Inflow);

        Assert.Equal(10, item.QuantityDeltaBase);
        Assert.Equal(1_000m, item.UnitCostSnapshot);
        Assert.Equal(10_000m, item.TotalCostSnapshot);
        Assert.Equal(10, item.QuantityOnHandAfter);
        Assert.Equal(10_000m, item.TotalCostValueAfter);
    }

    [Theory]
    [InlineData(StockMovementType.StockIn, false)]
    [InlineData(StockMovementType.ReturnIn, false)]
    [InlineData(StockMovementType.AdjustmentIn, false)]
    [InlineData(StockMovementType.Sale, true)]
    [InlineData(StockMovementType.AdjustmentOut, true)]
    public void Quantity_direction_must_match_movement_type(StockMovementType type, bool isOutbound)
    {
        var movement = CreateMovement(type);

        Assert.Throws<DomainException>(() => movement.AddItem(Guid.NewGuid(), isOutbound ? Inflow : Outflow));
        movement.AddItem(Guid.NewGuid(), isOutbound ? Outflow : Inflow);
    }

    [Fact]
    public void Only_a_reversal_references_the_reversed_movement()
    {
        Assert.Throws<DomainException>(() => CreateMovement(StockMovementType.Reversal));
        Assert.Throws<DomainException>(() => CreateMovement(StockMovementType.StockIn, reversalOf: Guid.NewGuid()));

        var reversal = CreateMovement(StockMovementType.Reversal, reversalOf: Guid.NewGuid());
        reversal.AddItem(Guid.NewGuid(), Outflow);
    }

    [Fact]
    public void Posting_requires_items()
    {
        var movement = CreateMovement(StockMovementType.StockIn);

        Assert.Throws<DomainException>(() => movement.Post(StaffId, Now));
        Assert.Equal(StockMovementStatus.Draft, movement.Status);
    }

    [Fact]
    public void Posted_movement_is_immutable_and_can_only_be_reversed()
    {
        var movement = CreateMovement(StockMovementType.StockIn);
        var item = movement.AddItem(Guid.NewGuid(), Inflow);

        movement.Post(StaffId, Now);

        Assert.Equal(StockMovementStatus.Posted, movement.Status);
        Assert.Throws<DomainException>(() => movement.AddItem(Guid.NewGuid(), Inflow));
        Assert.Throws<DomainException>(movement.Cancel);
        Assert.Throws<DomainException>(() => movement.MarkDeleted(StaffId, Now));
        Assert.Throws<DomainException>(() => item.MarkDeleted(StaffId, Now));

        movement.MarkReversed();
        Assert.Equal(StockMovementStatus.Reversed, movement.Status);
    }

    [Fact]
    public void Draft_movement_cannot_be_marked_reversed()
    {
        var movement = CreateMovement(StockMovementType.StockIn);

        Assert.Throws<DomainException>(movement.MarkReversed);
    }
}
