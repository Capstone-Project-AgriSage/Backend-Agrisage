using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.UnitTests.Domain.Features.Debt;

// Crop seasons for debt collection (F3.8). Dates follow the worked example of the "Công nợ theo vụ mùa" document
// (farmer Bảy): Đông Xuân 2026–2027 harvests 15/02–15/03/2027 with 30 grace days, so its payment is due 14/04/2027;
// Hè Thu 2027 is due 14/09/2027.
public class CropSeasonTests
{
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2027, 5, 1, 3, 0, 0, TimeSpan.Zero);

    private static CropSeason DongXuan() => new(Store, "DX-2026", "Đông Xuân 2026–2027", "RICE",
        new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30);

    private static CropSeason HeThu() => new(Store, "HT-2027", "Hè Thu 2027", "RICE",
        new DateOnly(2027, 2, 15), new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);

    [Fact]
    public void Payment_due_date_is_harvest_end_plus_grace_days()
    {
        Assert.Equal(new DateOnly(2027, 4, 14), DongXuan().PaymentDueDate);
        Assert.Equal(new DateOnly(2027, 9, 14), HeThu().PaymentDueDate);
    }

    [Fact]
    public void Zero_grace_days_and_a_leap_day_are_counted_exactly()
    {
        var noGrace = new CropSeason(Store, "Z", "Z", "RICE", new DateOnly(2027, 10, 1), new DateOnly(2028, 1, 10), new DateOnly(2028, 2, 10), 0);
        Assert.Equal(new DateOnly(2028, 2, 10), noGrace.PaymentDueDate);
        var leap = new CropSeason(Store, "L", "L", "RICE", new DateOnly(2027, 10, 1), new DateOnly(2028, 1, 10), new DateOnly(2028, 2, 10), 30);
        Assert.Equal(new DateOnly(2028, 3, 11), leap.PaymentDueDate); // 2028 has 29 February
    }

    [Theory]
    [InlineData("2026-10-15", "2027-02-15", "2027-02-15", 30)] // harvest end must be after harvest start
    [InlineData("2026-10-15", "2027-02-15", "2027-02-14", 30)]
    [InlineData("2027-02-15", "2027-02-15", "2027-03-15", 30)] // order window must open before harvest
    [InlineData("2026-10-15", "2027-02-15", "2027-03-15", -1)] // grace days 0..365
    [InlineData("2026-10-15", "2027-02-15", "2027-03-15", 366)]
    public void Invalid_dates_or_grace_days_are_rejected(string window, string harvestStart, string harvestEnd, int grace)
    {
        Assert.Throws<DomainException>(() => new CropSeason(Store, "X", "X", "RICE",
            DateOnly.Parse(window), DateOnly.Parse(harvestStart), DateOnly.Parse(harvestEnd), grace));
    }

    [Fact]
    public void Code_name_and_crop_type_are_required_and_trimmed()
    {
        Assert.Throws<DomainException>(() => new CropSeason(Store, " ", "N", "RICE", new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30));
        Assert.Throws<DomainException>(() => new CropSeason(Store, "C", "", "RICE", new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30));
        Assert.Throws<DomainException>(() => new CropSeason(Store, "C", "N", " ", new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30));
        var season = new CropSeason(Store, " DX-2026 ", " Đông Xuân ", " RICE ", new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30);
        Assert.Equal(("DX-2026", "Đông Xuân", "RICE"), (season.Code, season.Name, season.CropType));
    }

    // D2: the phase is derived from the Vietnam date, never stored. The harvest end day is still HARVESTING.
    [Theory]
    [InlineData("2026-10-14", CropSeasonPhase.Planned)]
    [InlineData("2026-10-15", CropSeasonPhase.Open)]
    [InlineData("2027-02-14", CropSeasonPhase.Open)]
    [InlineData("2027-02-15", CropSeasonPhase.Harvesting)]
    [InlineData("2027-03-15", CropSeasonPhase.Harvesting)]
    [InlineData("2027-03-16", CropSeasonPhase.Collecting)]
    [InlineData("2027-12-31", CropSeasonPhase.Collecting)]
    public void Phase_follows_the_calendar(string today, CropSeasonPhase expected) =>
        Assert.Equal(expected, DongXuan().PhaseOn(DateOnly.Parse(today)));

    [Fact]
    public void A_closed_season_is_closed_on_any_date_cannot_change_and_cannot_close_again()
    {
        var season = DongXuan();
        Assert.Throws<DomainException>(() => season.Close(Owner, Now, "{}", new DateOnly(2027, 3, 1))); // still harvesting
        season.Close(Owner, Now, "{\"originatedTotal\":2000000}", new DateOnly(2027, 5, 1));
        Assert.Equal(CropSeasonPhase.Closed, season.PhaseOn(new DateOnly(2026, 1, 1)));
        Assert.Equal((Owner, Now), (season.ClosedBy!.Value, season.ClosedAt!.Value));
        Assert.Equal("{\"originatedTotal\":2000000}", season.ClosedSummary);
        Assert.Throws<DomainException>(() => season.Close(Owner, Now, "{}", new DateOnly(2027, 5, 2)));
        Assert.Throws<DomainException>(() => season.Update("New", new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 20), 30));
        Assert.Throws<DomainException>(() => season.MoveOrderWindowStart(new DateOnly(2026, 10, 1)));
        Assert.Throws<DomainException>(() => season.LinkSeasonalRule(Guid.NewGuid()));
    }

    [Fact]
    public void Update_recomputes_the_due_date_and_keeps_the_dates_valid()
    {
        var season = DongXuan();
        season.Update("Đông Xuân (sửa)", new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 20), 15);
        Assert.Equal(new DateOnly(2027, 4, 4), season.PaymentDueDate);
        Assert.Throws<DomainException>(() => season.Update("X", new DateOnly(2026, 10, 15), new DateOnly(2027, 3, 20), 15)); // harvest start = window start
        Assert.Equal(new DateOnly(2027, 2, 20), season.HarvestStart);
    }

    [Fact]
    public void A_season_may_link_one_inventory_seasonal_rule_for_shared_statistics()
    {
        var season = DongXuan();
        Assert.Null(season.SeasonalRuleId);
        var rule = Guid.NewGuid();
        season.LinkSeasonalRule(rule);
        Assert.Equal(rule, season.SeasonalRuleId);
        season.LinkSeasonalRule(null);
        Assert.Null(season.SeasonalRuleId);
        Assert.Throws<DomainException>(() => season.LinkSeasonalRule(Guid.Empty));
    }

    // A credit order belongs to the season whose order window [orderWindowStart, harvestStart) contains its confirmation
    // date: buying while the rice is being harvested counts for the next season.
    [Theory]
    [InlineData("2026-10-14", null)]       // before the first season
    [InlineData("2026-10-15", "DX-2026")]
    [InlineData("2026-12-06", "DX-2026")]  // khoản A
    [InlineData("2027-01-11", "DX-2026")]  // khoản B
    [InlineData("2027-02-14", "DX-2026")]
    [InlineData("2027-02-15", "HT-2027")]
    [InlineData("2027-02-20", "HT-2027")]  // khoản C, confirmed during the harvest
    [InlineData("2027-07-14", "HT-2027")]
    [InlineData("2027-07-15", null)]       // after the last season: credit-tier term applies
    public void Resolve_picks_the_season_whose_order_window_contains_the_date(string date, string? code)
    {
        var seasons = new[] { HeThu(), DongXuan() };
        Assert.Equal(code, SeasonCalendar.Resolve(seasons, DateOnly.Parse(date))?.Code);
    }

    [Fact]
    public void Resolve_ignores_closed_seasons()
    {
        var season = DongXuan();
        season.Close(Owner, Now, "{}", new DateOnly(2027, 5, 1));
        Assert.Null(SeasonCalendar.Resolve([season], new DateOnly(2026, 12, 6)));
    }

    [Fact]
    public void Sequence_must_chain_without_gap_or_overlap_and_codes_must_be_unique()
    {
        SeasonCalendar.ValidateSequence([HeThu(), DongXuan()]);
        SeasonCalendar.ValidateSequence([]);
        var gap = new CropSeason(Store, "HT-2027", "Hè Thu", "RICE", new DateOnly(2027, 2, 20), new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);
        Assert.Throws<DomainException>(() => SeasonCalendar.ValidateSequence([DongXuan(), gap]));
        var overlap = new CropSeason(Store, "HT-2027", "Hè Thu", "RICE", new DateOnly(2027, 2, 1), new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);
        Assert.Throws<DomainException>(() => SeasonCalendar.ValidateSequence([DongXuan(), overlap]));
        var sameCode = new CropSeason(Store, "DX-2026", "Hè Thu", "RICE", new DateOnly(2027, 2, 15), new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15), 30);
        Assert.Throws<DomainException>(() => SeasonCalendar.ValidateSequence([DongXuan(), sameCode]));
    }

    [Fact]
    public void Next_order_window_starts_at_the_previous_harvest_start()
    {
        Assert.Equal(new DateOnly(2027, 2, 15), SeasonCalendar.NextOrderWindowStart([DongXuan()]));
        Assert.Equal(new DateOnly(2027, 7, 15), SeasonCalendar.NextOrderWindowStart([HeThu(), DongXuan()]));
        Assert.Null(SeasonCalendar.NextOrderWindowStart([]));
    }

    // D4: the optional cap shortens the term to delivery + cap days; the season due date is otherwise the same for every
    // delivery. Proposal (pending lead): a delivery after the season due date is not overdue on its first day.
    [Theory]
    [InlineData("2026-12-06", null, "2027-04-14")]
    [InlineData("2026-12-06", 60, "2027-02-04")]
    [InlineData("2027-03-01", 60, "2027-04-14")]
    [InlineData("2027-05-01", null, "2027-05-01")]
    public void Due_date_uses_the_season_due_date_with_an_optional_cap(string fulfilled, int? cap, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), SeasonCalendar.DueDate(new DateOnly(2027, 4, 14), DateOnly.Parse(fulfilled), cap));

    [Fact]
    public void Due_date_cap_must_be_positive()
    {
        Assert.Throws<DomainException>(() => SeasonCalendar.DueDate(new DateOnly(2027, 4, 14), new DateOnly(2027, 1, 1), 0));
    }

    // CS-11: the seven reason codes of the policy document; rollover accepts only the hardship codes (owner L3 choice,
    // two lists in one column).
    [Fact]
    public void Reason_codes_match_the_policy_and_rollover_accepts_only_hardship_codes()
    {
        Assert.Equal(7, Enum.GetValues<DebtReasonCode>().Length);
        Assert.All(Enum.GetValues<DebtReasonCode>(), c => Assert.True(DebtReasonCodes.AllowsReduction(c)));
        Assert.Equal(new[] { DebtReasonCode.MatMua, DebtReasonCode.ThienTai, DebtReasonCode.KhoKhan, DebtReasonCode.Khac },
            Enum.GetValues<DebtReasonCode>().Where(DebtReasonCodes.AllowsRollover));
    }
}
