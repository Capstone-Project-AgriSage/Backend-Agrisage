using AgriSage.Application.Features.Orders;

namespace AgriSage.UnitTests.Application.Orders;

// FEFO allocation (decision D6) as a pure function.
public class FefoAllocatorTests
{
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static FefoLot Lot(string tag, DateOnly? expiry, long available, int createdDays = 0, Guid? product = null, Guid? id = null) =>
        new(id ?? NamedId(tag), product ?? Product, expiry, Created.AddDays(createdDays), available);

    private static Guid NamedId(string tag) => new(tag.PadLeft(32, '0'));

    private static FefoDemand Demand(long quantity, Guid? product = null) => new(Guid.NewGuid(), product ?? Product, quantity);

    private static DateOnly Day(int offset) => new DateOnly(2026, 10, 3).AddDays(offset);

    [Fact]
    public void The_earliest_expiry_is_used_first_and_the_rest_comes_from_the_next_lot()
    {
        var late = Lot("2", Day(90), 80);
        var early = Lot("1", Day(30), 100);

        var result = Assert.Single(FefoAllocator.Allocate([Demand(125)], [late, early]));

        Assert.Equal([new FefoPick(early.LotId, 100), new FefoPick(late.LotId, 25)], result.Picks);
        Assert.Equal(0, result.ShortageBaseQuantity);
    }

    [Fact]
    public void Lots_without_an_expiry_come_last_whatever_their_age()
    {
        var undatedOld = Lot("1", null, 50, createdDays: -100);
        var dated = Lot("2", Day(365), 50);

        var result = Assert.Single(FefoAllocator.Allocate([Demand(60)], [undatedOld, dated]));

        Assert.Equal([new FefoPick(dated.LotId, 50), new FefoPick(undatedOld.LotId, 10)], result.Picks);
    }

    [Fact]
    public void The_same_expiry_falls_back_to_the_oldest_lot_then_to_the_lot_id()
    {
        var newer = Lot("1", Day(30), 10, createdDays: 5);
        var older = Lot("2", Day(30), 10, createdDays: 1);
        var sameAgeLowId = Lot("3", Day(30), 10, createdDays: 1);
        var sameAgeHighId = Lot("4", Day(30), 10, createdDays: 1);

        var result = Assert.Single(FefoAllocator.Allocate([Demand(35)], [newer, sameAgeHighId, older, sameAgeLowId]));

        Assert.Equal([older.LotId, sameAgeLowId.LotId, sameAgeHighId.LotId, newer.LotId], result.Picks.Select(p => p.LotId));
        Assert.Equal([10, 10, 10, 5], result.Picks.Select(p => p.BaseQuantity));
    }

    [Fact]
    public void A_shortage_is_reported_with_what_could_be_covered()
    {
        var result = Assert.Single(FefoAllocator.Allocate([Demand(150)], [Lot("1", Day(30), 100), Lot("2", Day(60), 20), Lot("3", Day(90), 0)]));

        Assert.Equal(120, result.Picks.Sum(p => p.BaseQuantity));
        Assert.Equal(30, result.ShortageBaseQuantity);
        Assert.Equal(30, Assert.Single(FefoAllocator.Allocate([Demand(30)], [])).ShortageBaseQuantity);
    }

    [Fact]
    public void Lines_of_the_same_product_share_the_available_stock_in_order()
    {
        var lot = Lot("1", Day(30), 100);
        var first = Demand(70);
        var second = Demand(70);

        var result = FefoAllocator.Allocate([first, second], [lot]);

        Assert.Equal((70, 0), (result[0].Picks.Sum(p => p.BaseQuantity), result[0].ShortageBaseQuantity));
        Assert.Equal((30, 40), (result[1].Picks.Sum(p => p.BaseQuantity), result[1].ShortageBaseQuantity));
        Assert.Equal([first.OrderItemId, second.OrderItemId], result.Select(r => r.OrderItemId));
    }

    [Fact]
    public void Lots_of_another_product_are_never_used()
    {
        var other = Guid.NewGuid();

        var result = Assert.Single(FefoAllocator.Allocate([Demand(10)], [Lot("1", Day(30), 100, product: other)]));

        Assert.Empty(result.Picks);
        Assert.Equal(10, result.ShortageBaseQuantity);

        var both = FefoAllocator.Allocate([Demand(10), Demand(5, other)], [Lot("1", Day(30), 100, product: other), Lot("2", Day(60), 8)]);
        Assert.Equal(new FefoPick(NamedId("2"), 8), Assert.Single(both[0].Picks));
        Assert.Equal(2, both[0].ShortageBaseQuantity);
        Assert.Equal(new FefoPick(NamedId("1"), 5), Assert.Single(both[1].Picks));
    }
}
