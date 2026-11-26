using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.UnitTests.Application.Inventory;

// F4.7 rules (FLOW_4 "Seasonal product suggestions"): the history window, what may be suggested, how it is grouped
// and scored, the reasons and the numbers.
public class SeasonalSuggestionRulesTests
{
    [Fact]
    public void The_history_window_is_the_same_days_of_the_latest_finished_year()
    {
        var today = new DateOnly(2026, 11, 25);
        Assert.Equal((new DateOnly(2025, 12, 10), new DateOnly(2026, 2, 20)),
            SeasonalSuggestionRules.ComparedWindow(new DateOnly(2026, 12, 10), new DateOnly(2027, 2, 20), today));

        // A season more than a year ahead reads two years back, so the window has ended.
        Assert.Equal((new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 31)),
            SeasonalSuggestionRules.ComparedWindow(new DateOnly(2027, 11, 1), new DateOnly(2027, 12, 31), today));

        // An ongoing season compares with last year's same days.
        Assert.Equal((new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 15)),
            SeasonalSuggestionRules.ComparedWindow(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 15), today));

        // 29 Feb reads as 28 Feb.
        Assert.Equal((new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 10)),
            SeasonalSuggestionRules.ComparedWindow(new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 10), new DateOnly(2027, 11, 1)));
    }

    [Fact]
    public void Observed_days_start_at_the_first_movement_of_the_product()
    {
        var from = new DateOnly(2025, 9, 1);
        var to = new DateOnly(2025, 10, 31);
        Assert.Equal(61, SeasonalSuggestionRules.ObservedDays(from, to, new DateOnly(2024, 1, 1)));
        Assert.Equal(31, SeasonalSuggestionRules.ObservedDays(from, to, new DateOnly(2025, 10, 1)));
        Assert.Equal(0, SeasonalSuggestionRules.ObservedDays(from, to, new DateOnly(2025, 11, 1)));
        Assert.Equal(0, SeasonalSuggestionRules.ObservedDays(from, to, null));
    }

    [Theory]
    [InlineData("Phân bón", SeasonalProductGroup.Fertilizer)]
    [InlineData("PHÂN BÓN LÁ", SeasonalProductGroup.Fertilizer)]
    [InlineData("Phân NPK", SeasonalProductGroup.Fertilizer)]
    [InlineData("Thuốc BVTV", SeasonalProductGroup.CropProtection)]
    [InlineData("Thuốc trừ sâu", SeasonalProductGroup.CropProtection)]
    [InlineData("Hạt giống", SeasonalProductGroup.Seed)]
    [InlineData("Giống lúa", SeasonalProductGroup.Seed)]
    [InlineData("Dụng cụ", SeasonalProductGroup.Other)]
    [InlineData(null, SeasonalProductGroup.Other)]
    public void A_category_name_gives_a_best_effort_group(string? category, SeasonalProductGroup expected) =>
        Assert.Equal(expected, SeasonalSuggestionRules.CategoryGroup(category));

    [Fact]
    public void Crop_protection_needs_an_approved_link_for_the_crop()
    {
        // An active ingredient, a protection link for another crop, or a pesticide category make crop protection.
        Assert.True(SeasonalSuggestionRules.IsCropProtection(true, false, "Phân bón"));
        Assert.True(SeasonalSuggestionRules.IsCropProtection(false, true, null));
        Assert.True(SeasonalSuggestionRules.IsCropProtection(false, false, "Thuốc BVTV"));
        Assert.False(SeasonalSuggestionRules.IsCropProtection(false, false, "Phân bón"));

        Assert.False(SeasonalSuggestionRules.IsEligible(isCropProtection: true, approvedLinkForCrop: false));
        Assert.True(SeasonalSuggestionRules.IsEligible(isCropProtection: true, approvedLinkForCrop: true));
        Assert.True(SeasonalSuggestionRules.IsEligible(isCropProtection: false, approvedLinkForCrop: false));
    }

    [Fact]
    public void The_server_groups_by_links_first_then_by_category()
    {
        Assert.Equal(SeasonalProductGroup.CropProtection, SeasonalSuggestionRules.Group(true, false, false, "Phân bón"));
        Assert.Equal(SeasonalProductGroup.CropProtection, SeasonalSuggestionRules.Group(false, false, true, null));
        Assert.Equal(SeasonalProductGroup.Fertilizer, SeasonalSuggestionRules.Group(false, true, false, "Khác"));
        Assert.Equal(SeasonalProductGroup.Seed, SeasonalSuggestionRules.Group(false, false, false, "Hạt giống"));
        Assert.Equal(SeasonalProductGroup.Other, SeasonalSuggestionRules.Group(false, false, false, "Dụng cụ"));
    }

    [Fact]
    public void The_data_score_and_confidence_follow_the_signals()
    {
        Assert.Equal(40 + 25 + 20 + 10 + 15, SeasonalSuggestionRules.Score(5, true, 99, 99, true, inStore: true));
        Assert.Equal(25 + 3 - 5, SeasonalSuggestionRules.Score(null, true, 3, 0, false, inStore: false));
        Assert.Equal(0, SeasonalSuggestionRules.Score(0, false, 0, 0, false, inStore: true));

        Assert.Equal(SeasonalSuggestionConfidence.High, SeasonalSuggestionRules.DataConfidence(10, true, 0, false));
        Assert.Equal(SeasonalSuggestionConfidence.High, SeasonalSuggestionRules.DataConfidence(10, false, 0, true));
        Assert.Equal(SeasonalSuggestionConfidence.Medium, SeasonalSuggestionRules.DataConfidence(10, false, 0, false));
        Assert.Equal(SeasonalSuggestionConfidence.Medium, SeasonalSuggestionRules.DataConfidence(null, true, 2, false));
        Assert.Equal(SeasonalSuggestionConfidence.Low, SeasonalSuggestionRules.DataConfidence(null, true, 0, false));
    }

    [Fact]
    public void The_target_comes_from_the_replenishment_rules_and_is_null_on_sparse_history()
    {
        // 600 sold over 60 observed days on 10 sale days: 10 a day × (7 lead + 14 coverage) + 5 safety.
        Assert.Equal(215, SeasonalSuggestionRules.SuggestedTarget(600, 10, 60, null, 7, 14, 5));
        // The minimum wins when it is higher.
        Assert.Equal(300, SeasonalSuggestionRules.SuggestedTarget(600, 10, 60, 300, 7, 14, 5));
        // Fewer than 14 observed days or 3 sale days, or no history at all: no number.
        Assert.Null(SeasonalSuggestionRules.SuggestedTarget(600, 10, 13, null, 7, 14, 5));
        Assert.Null(SeasonalSuggestionRules.SuggestedTarget(600, 2, 60, null, 7, 14, 5));
        Assert.Null(SeasonalSuggestionRules.SuggestedTarget(null, 0, 0, 100, 7, 14, 5));

        Assert.Equal(new DateOnly(2026, 11, 19), SeasonalSuggestionRules.OrderByDate(new DateOnly(2026, 12, 10), 14, 7));
    }

    [Fact]
    public void Evidence_is_written_from_data_and_skips_store_facts_for_a_product_not_carried()
    {
        var problems = new List<SeasonalSuggestionRules.ProblemCount> { new("Đạo ôn", 8), new("Rầy nâu", 0) };
        var inStore = SeasonalSuggestionRules.Evidence(true, 120, "Bao", new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 31),
            "Lúa", problems, 3, new("Thu Đông 2025", new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 31)), 40);
        Assert.Equal(["SALES", "LINK", "DIAGNOSIS", "ADVISORY", "RULE", "STOCK"], inStore.Select(e => e.Kind));
        Assert.Equal("Bán 120 bao từ 01/09/2025 đến 31/10/2025 (cùng kỳ năm trước).", inStore[0].Text);
        Assert.Equal("Liên kết đã duyệt cho Lúa: Đạo ôn, Rầy nâu.", inStore[1].Text);
        Assert.Equal("Ghi nhận 8 ca Đạo ôn trong cùng kỳ năm trước.", inStore[2].Text);

        var notCarried = SeasonalSuggestionRules.Evidence(false, null, null, new DateOnly(2025, 9, 1), new DateOnly(2025, 10, 31),
            "Lúa", problems, 0, null, null);
        Assert.Equal(["LINK", "DIAGNOSIS"], notCarried.Select(e => e.Kind));
    }

    [Fact]
    public void A_data_only_reason_names_the_strongest_signals()
    {
        Assert.Equal("Cùng kỳ năm trước đã bán 1.200 bao. Dùng cho Đạo ôn.",
            SeasonalSuggestionRules.DataReason(1200, "Bao", ["Đạo ôn"], 0, null, inStore: true));
        Assert.Equal("Dùng cho Đạo ôn, Rầy nâu, Đốm nâu và 1 vấn đề khác. Cửa hàng chưa kinh doanh sản phẩm này.",
            SeasonalSuggestionRules.DataReason(null, null, ["Đạo ôn", "Rầy nâu", "Đốm nâu", "Khô vằn"], 0, null, inStore: false));
    }

    [Fact]
    public void The_demand_factor_is_the_median_season_to_baseline_ratio_of_the_products_with_history()
    {
        // One product sold 2.4 times faster in the season: 2.4.
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(2.4m, true, 1),
            SeasonalSuggestionRules.DemandFactor([(4.8m, 2m)]));

        // Odd count: the middle ratio (1.5, 2, 3 → 2). Units never mix: each product is a ratio of its own.
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(2m, true, 3),
            SeasonalSuggestionRules.DemandFactor([(300m, 100m), (3m, 2m), (10m, 5m)]));

        // Even count: the mean of the two middle ratios (1.2, 1.6, 2.2, 4 → 1.9), rounded to one decimal.
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(1.9m, true, 4),
            SeasonalSuggestionRules.DemandFactor([(1.2m, 1m), (1.6m, 1m), (2.2m, 1m), (4m, 1m)]));
        Assert.Equal(1.3m, SeasonalSuggestionRules.DemandFactor([(1.25m, 1m)]).Factor); // half away from zero

        // A product without a daily demand in either window, or with no off-season sales, does not count.
        var sparse = SeasonalSuggestionRules.DemandFactor([(null, 2m), (3m, null), (3m, 0m), (6m, 2m)]);
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(3m, true, 1), sparse);

        // Kept in the range a seasonal rule accepts.
        Assert.Equal(5m, SeasonalSuggestionRules.DemandFactor([(40m, 1m)]).Factor);
        Assert.Equal(0.1m, SeasonalSuggestionRules.DemandFactor([(0m, 3m)]).Factor);
    }

    [Fact]
    public void Without_history_the_demand_factor_is_the_default()
    {
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(1.5m, false, 0), SeasonalSuggestionRules.DemandFactor([]));
        Assert.Equal(new SeasonalSuggestionRules.DemandFactorResult(1.5m, false, 0),
            SeasonalSuggestionRules.DemandFactor([(null, null), (2m, null)]));
    }

    [Fact]
    public void The_baseline_is_the_90_days_before_last_years_season_and_the_rule_is_named_after_the_season()
    {
        Assert.Equal((new DateOnly(2025, 9, 11), new DateOnly(2025, 12, 9)),
            SeasonalSuggestionRules.BaselineWindow(new DateOnly(2025, 12, 10)));
        Assert.Equal("Vụ Lúa 15/12/2026 – 14/02/2027",
            SeasonalSuggestionRules.RuleName("Lúa", new DateOnly(2026, 12, 15), new DateOnly(2027, 2, 14)));
        Assert.True(SeasonalSuggestionRules.RuleName(new string('x', 200), new DateOnly(2026, 12, 15), new DateOnly(2027, 2, 14)).Length <= 150);
    }

    [Fact]
    public void The_list_request_is_validated()
    {
        var list = new SeasonalSuggestionListRequestValidator();
        Assert.True(list.Validate(new SeasonalSuggestionListRequest { CropType = "RICE" }).IsValid);
        Assert.False(list.Validate(new SeasonalSuggestionListRequest { FromDate = new(2027, 2, 1), ToDate = new(2027, 1, 1) }).IsValid);
    }
}
