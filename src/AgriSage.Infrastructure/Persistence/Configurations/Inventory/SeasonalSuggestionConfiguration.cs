using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Inventory;

// inventory_seasonal_suggestion_runs (DATABASE_DESIGN §35.25, F4.7): one row per upcoming season the reminder prepared.
// Optional references are NO ACTION and required ones RESTRICT by convention.
internal sealed class SeasonalSuggestionRunConfiguration : EntityConfiguration<SeasonalSuggestionRun>
{
    protected override string TableName => "inventory_seasonal_suggestion_runs";

    protected override void ConfigureEntity(EntityTypeBuilder<SeasonalSuggestionRun> builder)
    {
        builder.Property(run => run.ScheduleKey).HasMaxLength(SeasonalSuggestionRun.MaxScheduleKeyLength).IsRequired();
        builder.Property(run => run.CropType).HasMaxLength(SeasonalSuggestionRun.MaxCropTypeLength).IsRequired();
        builder.Property(run => run.Region).HasMaxLength(SeasonalSuggestionRun.MaxRegionLength);
        builder.Property(run => run.Candidates).IsJson().IsRequired();

        builder.HasReference<SeasonalSuggestionRun, Store>(run => run.StoreId);
        builder.HasReference<SeasonalSuggestionRun, SeasonalRule>(run => run.SeasonalRuleId);
        builder.HasReference<SeasonalSuggestionRun, CropSeason>(run => run.CropSeasonId);
        builder.HasMany(run => run.Items).WithOne().HasForeignKey(item => item.RunId);
        builder.Navigation(run => run.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasCheck("dates", "ends_on >= starts_on");
        builder.HasCheck("compared_dates", "compared_to >= compared_from");
        builder.HasCheck("prepare_days", "prepare_days BETWEEN 0 AND 90");
        builder.HasCheck("candidate_count", "candidate_count >= 0");

        builder.HasIndex(run => new { run.StoreId, run.ScheduleKey }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(run => new { run.StoreId, run.StartsOn });
    }
}

// inventory_seasonal_suggestion_items (DATABASE_DESIGN §35.25): the ranked products of a run.
internal sealed class SeasonalSuggestionItemConfiguration : EntityConfiguration<SeasonalSuggestionItem>
{
    protected override string TableName => "inventory_seasonal_suggestion_items";

    protected override void ConfigureEntity(EntityTypeBuilder<SeasonalSuggestionItem> builder)
    {
        builder.Property(item => item.ProductGroup).HasMaxLength(20);
        builder.Property(item => item.Confidence).HasMaxLength(10);
        builder.Property(item => item.Reason).HasMaxLength(SeasonalSuggestionItem.MaxReasonLength).IsRequired();
        builder.Property(item => item.Evidence).IsJson().IsRequired();

        builder.HasReference<SeasonalSuggestionItem, Product>(item => item.ProductId);
        builder.HasReference<SeasonalSuggestionItem, StoreProduct>(item => item.StoreProductId);

        builder.HasCheck("rank", "\"rank\" > 0");
        builder.HasCheck("sold_last_season_base", "sold_last_season_base IS NULL OR sold_last_season_base >= 0");
        builder.HasCheck("suggested_target_base", "suggested_target_base IS NULL OR suggested_target_base >= 0");

        builder.HasIndex(item => new { item.RunId, item.ProductId }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(item => new { item.RunId, item.Rank }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
