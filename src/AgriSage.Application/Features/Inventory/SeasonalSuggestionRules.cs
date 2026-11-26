using System.Globalization;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Enums;

namespace AgriSage.Application.Features.Inventory;

// Pure rules of F4.7 (FLOW_4 "Seasonal product suggestions"): history window, eligibility, grouping, score,
// ranking confidence, reasons, evidence texts and quantities. No I/O.
public static class SeasonalSuggestionRules
{
    // Lead time assumed for a product the store does not carry yet.
    public const int DefaultLeadDays = 7;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    // The same calendar days in the latest year whose window ended before today (29 Feb reads as 28 Feb).
    public static (DateOnly From, DateOnly To) ComparedWindow(DateOnly startsOn, DateOnly endsOn, DateOnly today)
    {
        var years = 1;
        while (endsOn.AddYears(-years) >= today) years++;
        return (startsOn.AddYears(-years), endsOn.AddYears(-years));
    }

    // Days of the window a product had history for: from its first ledger movement (or the window start) to the end.
    public static int ObservedDays(DateOnly from, DateOnly to, DateOnly? firstMovementDay)
    {
        if (firstMovementDay is not { } first || first > to) return 0;
        var start = first > from ? first : from;
        return to.DayNumber - start.DayNumber + 1;
    }

    // Crop protection: a product with an active ingredient, a PROTECTION link for any crop, or a pesticide category.
    // It is suggested only with an approved link for the crop of the season.
    public static bool IsCropProtection(bool hasActiveIngredient, bool hasProtectionLink, string? categoryName) =>
        hasActiveIngredient || hasProtectionLink || CategoryGroup(categoryName) == SeasonalProductGroup.CropProtection;

    public static bool IsEligible(bool isCropProtection, bool approvedLinkForCrop) => !isCropProtection || approvedLinkForCrop;

    // The server's grouping; OTHER means it could not tell.
    public static SeasonalProductGroup Group(bool protectionLinkForCrop, bool nutritionLinkForCrop, bool isCropProtection,
        string? categoryName)
    {
        if (protectionLinkForCrop || isCropProtection) return SeasonalProductGroup.CropProtection;
        if (nutritionLinkForCrop) return SeasonalProductGroup.Fertilizer;
        var byCategory = CategoryGroup(categoryName);
        return byCategory == SeasonalProductGroup.CropProtection ? SeasonalProductGroup.Other : byCategory;
    }

    // A best-effort reading of a free category name ("Phân bón", "Thuốc BVTV", "Hạt giống", …), accents ignored.
    public static SeasonalProductGroup CategoryGroup(string? categoryName)
    {
        var name = Plain(categoryName);
        if (name.Length == 0) return SeasonalProductGroup.Other;
        if (name.Contains("thuoc") || name.Contains("bvtv") || name.Contains("bao ve thuc vat") || name.Contains("pesticide"))
            return SeasonalProductGroup.CropProtection;
        if (name.Contains("phan bon") || name.StartsWith("phan ", StringComparison.Ordinal) || name == "phan"
            || name.Contains("fertili"))
            return SeasonalProductGroup.Fertilizer;
        if (name.Contains("giong") || name.Contains("seed")) return SeasonalProductGroup.Seed;
        return SeasonalProductGroup.Other;
    }

    // Fixed data score: what sold last season, an approved link, how often its problems were found, advice given and
    // last seasons' rules. A product the store does not carry ranks a little lower.
    public static int Score(long? sold, bool approvedLink, int problemCases, int advised, bool inPastRule, bool inStore) =>
        (sold > 0 ? 40 : 0) + (approvedLink ? 25 : 0) + Math.Min(20, Math.Max(0, problemCases))
        + Math.Min(10, Math.Max(0, advised)) + (inPastRule ? 15 : 0) - (inStore ? 0 : 5);

    public static SeasonalSuggestionConfidence DataConfidence(long? sold, bool approvedLink, int problemCases, bool inPastRule) =>
        sold > 0 && (approvedLink || inPastRule) ? SeasonalSuggestionConfidence.High
        : sold > 0 || (approvedLink && problemCases > 0) ? SeasonalSuggestionConfidence.Medium
        : SeasonalSuggestionConfidence.Low;

    // The reason of a data-only item: its strongest signals, in Vietnamese.
    public static string DataReason(long? sold, string? unitName, IReadOnlyList<string> problems, int advised,
        string? pastRule, bool inStore)
    {
        var parts = new List<string>();
        if (sold > 0) parts.Add($"Cùng kỳ năm trước đã bán {Number(sold.Value)} {Unit(unitName)}.");
        if (problems.Count > 0) parts.Add($"Dùng cho {Names(problems)}.");
        if (pastRule is not null) parts.Add($"Có trong quy tắc mùa vụ “{pastRule}”.");
        if (advised > 0) parts.Add($"Đã tư vấn cho nông dân {Number(advised)} lần trong 12 tháng qua.");
        if (!inStore) parts.Add("Cửa hàng chưa kinh doanh sản phẩm này.");
        var reason = parts.Count == 0 ? "Phù hợp với mùa vụ theo dữ liệu của cửa hàng." : string.Join(" ", parts.Take(3));
        return Clip(reason, SeasonalSuggestionItemLimits.ReasonLength);
    }

    public sealed record ProblemCount(string Name, int Cases);

    public sealed record RuleMention(string Name, DateOnly StartsOn, DateOnly EndsOn);

    // Evidence of one item, written from data (kinds SALES, LINK, DIAGNOSIS, ADVISORY, RULE, STOCK).
    public static IReadOnlyList<SeasonalSuggestionEvidence> Evidence(
        bool inStore,
        long? sold,
        string? unitName,
        DateOnly comparedFrom,
        DateOnly comparedTo,
        string cropName,
        IReadOnlyList<ProblemCount> linkedProblems,
        int advised,
        RuleMention? pastRule,
        long? sellable)
    {
        var evidence = new List<SeasonalSuggestionEvidence>();
        if (inStore)
        {
            evidence.Add(new("SALES", sold > 0
                ? $"Bán {Number(sold!.Value)} {Unit(unitName)} từ {Day(comparedFrom)} đến {Day(comparedTo)} (cùng kỳ năm trước)."
                : $"Không bán được trong cùng kỳ năm trước ({Day(comparedFrom)} – {Day(comparedTo)})."));
        }

        if (linkedProblems.Count > 0)
            evidence.Add(new("LINK", $"Liên kết đã duyệt cho {cropName}: {Names(linkedProblems.Select(p => p.Name).ToList())}."));

        var found = linkedProblems.Where(p => p.Cases > 0).OrderByDescending(p => p.Cases).ToList();
        if (found.Count > 0)
        {
            var text = string.Join(", ", found.Take(3).Select(p => $"{Number(p.Cases)} ca {p.Name}"));
            evidence.Add(new("DIAGNOSIS", $"Ghi nhận {text} trong cùng kỳ năm trước."));
        }

        if (advised > 0) evidence.Add(new("ADVISORY", $"Đã tư vấn cho nông dân {Number(advised)} lần trong 12 tháng qua."));
        if (pastRule is not null)
        {
            evidence.Add(new("RULE",
                $"Có trong quy tắc mùa vụ “{pastRule.Name}” ({Day(pastRule.StartsOn)} – {Day(pastRule.EndsOn)})."));
        }

        if (inStore && sellable is { } stock)
            evidence.Add(new("STOCK", $"Tồn có thể bán lúc tạo gợi ý: {Number(stock)} {Unit(unitName)}."));
        return evidence;
    }

    // Stock to hold during the season by the replenishment rules: the daily demand of the history window (only with
    // at least 14 observed days and 3 sale days) over lead time and coverage, plus safety, at least the minimum.
    public static long? SuggestedTarget(long? sold, int saleDays, int observedDays, long? minimum, int leadDays,
        int coverageDays, long safety)
    {
        if (sold is null) return null;
        var daily = ReplenishmentMath.DailyDemand(sold.Value, observedDays, saleDays);
        return daily is null ? null : ReplenishmentMath.Target(null, minimum, daily, leadDays, coverageDays, safety, 1m, null);
    }

    public static DateOnly OrderByDate(DateOnly startsOn, int prepareDays, int leadDays) =>
        startsOn.AddDays(-(prepareDays + Math.Max(0, leadDays)));

    // Demand factor of a seasonal rule filled in from a suggestion, when no product has enough history.
    public const decimal DefaultDemandFactor = 1.5m;

    // The range a seasonal rule accepts (SeasonalRule, SeasonalRuleWriteRequestValidator).
    public const decimal MinDemandFactor = 0.1m;
    public const decimal MaxDemandFactor = 5m;

    // Length of the off-season window compared with the season: the replenishment forecast reads the last 90 days by
    // default, and a seasonal rule multiplies that usual demand, so the factor compares the season with the same span.
    public const int BaselineDays = 90;

    public sealed record DemandFactorResult(decimal Factor, bool FromHistory, int Products);

    // The 90 days just before last year's season.
    public static (DateOnly From, DateOnly To) BaselineWindow(DateOnly comparedFrom) =>
        (comparedFrom.AddDays(-BaselineDays), comparedFrom.AddDays(-1));

    // How much faster the products sold in last year's season than in the 90 days before it: the median of the
    // per-product ratios (units differ between products, so quantities are never added up), over the products with a
    // daily demand in both windows (ReplenishmentMath.DailyDemand: ≥ 14 observed days and ≥ 3 sale days) and some
    // off-season sales. Rounded to one decimal and kept in the range of a rule. Without such a product: 1.5.
    public static DemandFactorResult DemandFactor(IEnumerable<(decimal? Season, decimal? Baseline)> rates)
    {
        var ratios = rates.Where(r => r.Season is not null && r.Baseline is > 0)
            .Select(r => r.Season!.Value / r.Baseline!.Value).Order().ToList();
        if (ratios.Count == 0) return new(DefaultDemandFactor, false, 0);
        var middle = ratios.Count / 2;
        var median = ratios.Count % 2 == 1 ? ratios[middle] : (ratios[middle - 1] + ratios[middle]) / 2;
        var factor = Math.Clamp(Math.Round(median, 1, MidpointRounding.AwayFromZero), MinDemandFactor, MaxDemandFactor);
        return new(factor, true, ratios.Count);
    }

    // "Vụ Lúa 15/12/2026 – 14/02/2027", within the 150 characters of a rule name.
    public static string RuleName(string cropName, DateOnly startsOn, DateOnly endsOn) =>
        Clip(Squash($"Vụ {cropName} {Day(startsOn)} – {Day(endsOn)}"), 150);

    public static string Day(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString("N0", Vietnamese);

    private static string Unit(string? unitName) => string.IsNullOrWhiteSpace(unitName) ? "đơn vị" : unitName.Trim().ToLower(Vietnamese);

    private static string Names(IReadOnlyList<string> names) => names.Count <= 3
        ? string.Join(", ", names)
        : $"{string.Join(", ", names.Take(3))} và {names.Count - 3} vấn đề khác";

    private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Clip(string text, int maxLength) => text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";

    // Lower case without Vietnamese accents ("Phân bón" → "phan bon").
    private static string Plain(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        return Squash(plain.ToString().Normalize(NormalizationForm.FormC));
    }
}

// Limits shared with the Domain item without making the rules depend on its constructor.
internal static class SeasonalSuggestionItemLimits
{
    public const int ReasonLength = AgriSage.Domain.Features.Inventory.Entities.SeasonalSuggestionItem.MaxReasonLength;
}
