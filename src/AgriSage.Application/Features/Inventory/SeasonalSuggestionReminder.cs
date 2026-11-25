using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.Application.Features.Inventory;

// A season that starts soon and that no active seasonal rule of the same crop covers yet (F4.7 rule 8).
public sealed record UpcomingSeason(
    string Key,
    string Crop,
    string? Region,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int PrepareDays,
    Guid? SeasonalRuleId,
    Guid? CropSeasonId);

// The reminder of F4.7, run by the background worker (one instance at a time). Upcoming seasons come from last year's
// seasonal rules of the Store (the same dates this year) and from open crop seasons without a linked rule. At the
// first mark it prepares one SCHEDULED run per season and notifies the holders of SEASONAL_RULES.MANAGE; at the second
// mark, while still no rule covers the season, it notifies once more. It never creates a rule or anything else.
public sealed class SeasonalSuggestionReminder(
    IAgriSageDbContext context,
    SeasonalSuggestionGenerator generator,
    NotificationWriter writer,
    AuditTrail audit,
    IOptions<SeasonalSuggestionOptions> options,
    IDateTimeProvider clock,
    ILogger<SeasonalSuggestionReminder> logger)
{
    public const string NotificationType = "SEASONAL_SUGGESTION_READY";
    public const string EntityType = "SEASONAL_SUGGESTION_RUN";
    public const string ManagePermission = "SEASONAL_RULES.MANAGE";

    public async Task RemindAsync(CancellationToken token)
    {
        var o = options.Value;
        var store = await ActiveStore.GetIdAsync(context, token);
        var today = BusinessCalendar.Today(clock.UtcNow);
        var seasons = await UpcomingAsync(store, today, o.FirstReminderDays, token);
        if (seasons.Count == 0) return;

        var recipients = await NotificationRecipients.WithPermissionAsync(context, store, ManagePermission, token);
        var province = await context.Stores.AsNoTracking().Where(s => s.Id == store).Select(s => s.Province).SingleAsync(token);
        foreach (var season in seasons.Take(o.MaxSeasonsPerRun))
        {
            var run = await context.SeasonalSuggestionRuns.AsNoTracking()
                .Where(r => r.StoreId == store && r.ScheduleKey == season.Key)
                .Select(r => new { r.Id, Items = r.Items.Count() }).SingleOrDefaultAsync(token);
            if (run is null)
            {
                var plan = await generator.PrepareAsync(store, season.Crop, season.Region ?? province, season.StartsOn,
                    season.EndsOn, season.PrepareDays, token);
                var created = SeasonalSuggestionRun.Create(store, season.Key, plan.Season, season.SeasonalRuleId,
                    season.CropSeasonId, plan.Candidates.Candidates.Count, plan.CandidatesJson, generator.Rank(plan));
                context.SeasonalSuggestionRuns.Add(created);
                audit.Record("SEASONAL_SUGGESTION_CREATED", EntityType, created.Id, store, newValues: new
                {
                    created.ScheduleKey, created.CropType, created.Region, created.StartsOn, created.EndsOn,
                    Items = created.Items.Count, created.CandidateCount
                });
                // The worker's lock runs one reminder at a time; the unique (store, schedule key) index is the backstop.
                await context.SaveChangesAsync(token);
                logger.LogInformation("Seasonal reminder: prepared {Items} suggestions for {Key}.",
                    created.Items.Count, season.Key);
                run = new { created.Id, Items = created.Items.Count };
            }

            if (recipients.Count == 0) continue;
            var crop = CropCatalog.Find(season.Crop)?.Name ?? season.Crop;
            var period = $"{SeasonalSuggestionRules.Day(season.StartsOn)} đến {SeasonalSuggestionRules.Day(season.EndsOn)}";
            var orderBy = SeasonalSuggestionRules.OrderByDate(season.StartsOn, season.PrepareDays, SeasonalSuggestionRules.DefaultLeadDays);
            await writer.AddAsync(recipients, NotificationType, $"Sắp vào vụ {crop}",
                run.Items > 0
                    ? $"Vụ {crop} từ {period}: đã có {run.Items} mặt hàng gợi ý nhập. Nên đặt hàng trước {SeasonalSuggestionRules.Day(orderBy)}."
                    : $"Vụ {crop} từ {period}: chưa đủ dữ liệu để gợi ý mặt hàng. Vui lòng kiểm tra danh mục và quy tắc mùa vụ.",
                $"seasonal-suggestion:{season.Key}:FIRST", EntityType, run.Id, token);

            var daysLeft = season.StartsOn.DayNumber - today.DayNumber;
            if (daysLeft <= o.SecondReminderDays)
            {
                await writer.AddAsync(recipients, NotificationType, $"Còn {daysLeft} ngày đến vụ {crop}",
                    $"Chưa có quy tắc mùa vụ nhập hàng cho vụ {crop} từ {period}. Xem {run.Items} mặt hàng gợi ý để chuẩn bị.",
                    $"seasonal-suggestion:{season.Key}:SECOND", EntityType, run.Id, token);
            }

            await context.SaveChangesAsync(token);
        }
    }

    // Seasons that start after today and within `days`, not covered by an active seasonal rule of the same crop.
    // Crop seasons come first; last year's rule of the same crop and overlapping dates is then the same season.
    public async Task<IReadOnlyList<UpcomingSeason>> UpcomingAsync(Guid store, DateOnly today, int days, CancellationToken token)
    {
        var horizon = today.AddDays(days);
        var active = (await context.SeasonalRules.AsNoTracking()
                .Where(r => r.StoreId == store && r.IsActive && r.EndsOn >= today)
                .Select(r => new { r.CropType, r.StartsOn, r.EndsOn }).ToListAsync(token))
            .Select(r => (Crop: KnownCrop(r.CropType), r.StartsOn, r.EndsOn)).ToList();
        bool Covered(string crop, DateOnly start, DateOnly end) =>
            active.Any(r => r.Crop == crop && r.StartsOn <= end && r.EndsOn >= start);

        var upcoming = new List<UpcomingSeason>();
        var cropSeasons = await context.CropSeasons.AsNoTracking()
            .Where(s => s.StoreId == store && s.ClosedAt == null && s.SeasonalRuleId == null
                && s.OrderWindowStart > today && s.OrderWindowStart <= horizon)
            .OrderBy(s => s.OrderWindowStart)
            .Select(s => new { s.Id, s.CropType, s.OrderWindowStart, s.HarvestStart }).ToListAsync(token);
        foreach (var s in cropSeasons)
        {
            if (KnownCrop(s.CropType) is not { } crop) continue;
            var ends = Earlier(s.HarvestStart.AddDays(-1), s.OrderWindowStart.AddDays(SeasonalSuggestionRun.MaxSeasonDays - 1));
            if (ends < s.OrderWindowStart || Covered(crop, s.OrderWindowStart, ends)) continue;
            upcoming.Add(new UpcomingSeason($"SEASON:{s.Id:N}", crop, null, s.OrderWindowStart, ends, 14, null, s.Id));
        }

        var lastYear = await context.SeasonalRules.AsNoTracking()
            .Where(r => r.StoreId == store && r.StartsOn > today.AddYears(-1) && r.StartsOn <= horizon.AddYears(-1))
            .OrderBy(r => r.StartsOn)
            .Select(r => new { r.Id, r.CropType, r.Region, r.StartsOn, r.EndsOn, r.PrepareDays }).ToListAsync(token);
        foreach (var r in lastYear)
        {
            if (KnownCrop(r.CropType) is not { } crop) continue;
            var starts = r.StartsOn.AddYears(1);
            var ends = Earlier(r.EndsOn.AddYears(1), starts.AddDays(SeasonalSuggestionRun.MaxSeasonDays - 1));
            if (starts <= today || starts > horizon || Covered(crop, starts, ends)) continue;
            if (upcoming.Any(u => u.Crop == crop && u.StartsOn <= ends && u.EndsOn >= starts)) continue;
            upcoming.Add(new UpcomingSeason($"RULE:{r.Id:N}:{starts.Year}", crop, r.Region, starts, ends, r.PrepareDays, r.Id, null));
        }

        return upcoming.OrderBy(u => u.StartsOn).ThenBy(u => u.Key, StringComparer.Ordinal).ToList();
    }

    // A rule's crop as a catalog code: the code itself or the crop's Vietnamese name ("Lúa" → RICE).
    public static string? KnownCrop(string? text)
    {
        var code = CropCatalog.Normalize(text);
        if (code is null) return null;
        if (CropCatalog.IsKnown(code)) return code;
        return CropCatalog.All.FirstOrDefault(c => string.Equals(c.Name, text!.Trim(), StringComparison.OrdinalIgnoreCase))?.Code;
    }

    private static DateOnly Earlier(DateOnly a, DateOnly b) => a < b ? a : b;
}
