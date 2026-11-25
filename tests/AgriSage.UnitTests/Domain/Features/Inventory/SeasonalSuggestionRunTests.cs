using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Domain.Features.Inventory;

// F4.7 (FLOW_4 "Seasonal product suggestions", DATABASE_DESIGN §35.25): a run keeps the season it is for, its key and
// the ranked products; it is written once.
public class SeasonalSuggestionRunTests
{
    private static readonly Guid Store = Guid.NewGuid();

    private static SeasonalSuggestionSeason Season(DateOnly? starts = null, DateOnly? ends = null, int prepare = 14,
        DateOnly? from = null, DateOnly? to = null) =>
        new("RICE", "  Cần Thơ ", starts ?? new DateOnly(2026, 12, 10), ends ?? new DateOnly(2027, 2, 20), prepare,
            from ?? new DateOnly(2025, 12, 10), to ?? new DateOnly(2026, 2, 20));

    private static SeasonalSuggestionLine Line(Guid? product = null, Guid? storeProduct = null, long? sold = 120, long? target = 200) =>
        new(product ?? Guid.NewGuid(), storeProduct, SeasonalProductGroup.Fertilizer, SeasonalSuggestionConfidence.High,
            "  Bán chạy cùng kỳ  ", "[]", storeProduct is null ? null : sold, storeProduct is null ? null : 40,
            storeProduct is null ? null : target, new DateOnly(2026, 11, 19));

    private static SeasonalSuggestionRun Run(IReadOnlyList<SeasonalSuggestionLine>? lines = null,
        SeasonalSuggestionSeason? season = null, string key = " RULE:abc:2027 ") =>
        SeasonalSuggestionRun.Create(Store, key, season ?? Season(), Guid.NewGuid(), null, 7, "{}",
            lines ?? [Line(storeProduct: Guid.NewGuid()), Line()]);

    [Fact]
    public void A_run_keeps_its_key_season_and_ranked_items()
    {
        var run = Run();

        Assert.Equal("RULE:abc:2027", run.ScheduleKey);
        Assert.Equal("Cần Thơ", run.Region);
        Assert.Equal(7, run.CandidateCount);
        Assert.Equal([1, 2], run.Items.Select(i => i.Rank));
        Assert.All(run.Items, i => Assert.Equal(run.Id, i.RunId));
        Assert.Equal("Bán chạy cùng kỳ", run.Items.First().Reason);
        Assert.Null(run.Items.Last().StoreProductId);
        Assert.Empty(Run([]).Items);
    }

    [Fact]
    public void A_run_needs_a_schedule_key_of_bounded_length()
    {
        Assert.Throws<DomainException>(() => Run(key: " "));
        Assert.Throws<DomainException>(() => Run(key: new string('k', SeasonalSuggestionRun.MaxScheduleKeyLength + 1)));
    }

    [Fact]
    public void The_season_and_history_window_must_be_consistent()
    {
        // 184 days, history after the start, preparation out of range, end before start.
        Assert.Throws<DomainException>(() => Run(season: Season(new DateOnly(2027, 1, 1), new DateOnly(2027, 7, 3))));
        Assert.Throws<DomainException>(() => Run(season: Season(from: new DateOnly(2026, 12, 1), to: new DateOnly(2026, 12, 20))));
        Assert.Throws<DomainException>(() => Run(season: Season(prepare: 91)));
        Assert.Throws<DomainException>(() => Run(season: Season(ends: new DateOnly(2026, 12, 9))));

        // 183 days is the longest season.
        Assert.Equal(new DateOnly(2027, 7, 2), Run(season: Season(new DateOnly(2027, 1, 1), new DateOnly(2027, 7, 2))).EndsOn);
    }

    [Fact]
    public void Items_are_distinct_and_a_product_not_carried_has_no_store_numbers()
    {
        var product = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Run([Line(product), Line(product)]));
        Assert.Throws<DomainException>(() => Run([Line() with { SoldLastSeasonBase = 5 }]));
        Assert.Throws<DomainException>(() => Run([Line(storeProduct: Guid.NewGuid(), target: -1)]));
        Assert.Throws<DomainException>(() => Run([Line() with { Reason = new string('x', SeasonalSuggestionItem.MaxReasonLength + 1) }]));
        Assert.Throws<DomainException>(() => Run(Enumerable.Range(0, SeasonalSuggestionRun.MaxItems + 1).Select(_ => Line()).ToList()));
    }
}
