using AgriSage.Domain.Features.Advisory;

namespace AgriSage.UnitTests.Domain.Features.Advisory;

public class DosePlannerTests
{
    private static readonly Guid Kg = Guid.NewGuid();
    private static readonly Guid Bag = Guid.NewGuid();

    private static DosePlanner.SalePack[] Fertilizer() =>
    [
        new(Kg, "Kilogram", 1, 15_000m),
        new(Bag, "Bao 50 kg", 50, 700_000m)
    ];

    [Theory]
    [InlineData(250, 1, 3_000, 75)]      // 250 kg/ha on 3 000 m2 = 75 kg
    [InlineData(250, 2, 3_000, 150)]     // two applications double it
    [InlineData(0.5, 1, 3_000, 1)]       // 0.15 bottle is rounded up to 1
    [InlineData(1, 1, 10_000, 1)]        // exactly one hectare
    [InlineData(1, 1, 10_001, 2)]        // a little over one hectare rounds up
    public void The_need_is_rounded_up_so_the_field_is_never_under_supplied(double dose, int applications, double area, long expected)
    {
        Assert.Equal(expected, DosePlanner.RequiredBaseQuantity((decimal)dose, applications, (decimal)area));
    }

    [Theory]
    [InlineData(null, 1, 3_000.0)]
    [InlineData(0.0, 1, 3_000.0)]
    [InlineData(250.0, 1, null)]
    [InlineData(250.0, 1, 0.0)]
    [InlineData(250.0, 0, 3_000.0)]
    public void Without_a_dose_an_area_or_an_application_there_is_no_need(double? dose, int applications, double? area)
    {
        Assert.Null(DosePlanner.RequiredBaseQuantity((decimal?)dose, applications, (decimal?)area));
    }

    [Fact]
    public void The_largest_packaging_is_used_first_and_the_remainder_in_the_smaller_ones()
    {
        var plan = DosePlanner.Split(75, Fertilizer())!;

        Assert.Collection(
            plan.Lines,
            bag => { Assert.Equal(Bag, bag.PackagingId); Assert.Equal(1, bag.Quantity); Assert.Equal(700_000m, bag.LineAmount); },
            kg => { Assert.Equal(Kg, kg.PackagingId); Assert.Equal(25, kg.Quantity); Assert.Equal(375_000m, kg.LineAmount); });
        Assert.Equal(75, plan.BaseQuantity);
        Assert.Equal(1_075_000m, plan.TotalAmount);
    }

    [Fact]
    public void The_last_remainder_is_rounded_up_in_the_smallest_packaging()
    {
        var packs = new DosePlanner.SalePack[] { new(Bag, "Hop 20 goi", 20, 95_000m) };

        var plan = DosePlanner.Split(25, packs)!;

        Assert.Single(plan.Lines);
        Assert.Equal(2, plan.Lines[0].Quantity);
        Assert.Equal(40, plan.BaseQuantity);
        Assert.Equal(190_000m, plan.TotalAmount);
    }

    [Fact]
    public void An_exact_multiple_of_a_packaging_needs_no_smaller_one()
    {
        var plan = DosePlanner.Split(100, Fertilizer())!;

        var line = Assert.Single(plan.Lines);
        Assert.Equal(Bag, line.PackagingId);
        Assert.Equal(2, line.Quantity);
    }

    [Fact]
    public void The_plan_never_supplies_less_than_the_need()
    {
        foreach (var need in Enumerable.Range(1, 400))
        {
            Assert.True(DosePlanner.Split(need, Fertilizer())!.BaseQuantity >= need, $"need {need}");
        }
    }

    [Fact]
    public void There_is_no_plan_without_a_need_or_a_priced_packaging()
    {
        Assert.Null(DosePlanner.Split(null, Fertilizer()));
        Assert.Null(DosePlanner.Split(0, Fertilizer()));
        Assert.Null(DosePlanner.Split(10, []));
        Assert.Null(DosePlanner.Split(10, [new DosePlanner.SalePack(Kg, "Kilogram", 0, 1m)]));
    }
}
