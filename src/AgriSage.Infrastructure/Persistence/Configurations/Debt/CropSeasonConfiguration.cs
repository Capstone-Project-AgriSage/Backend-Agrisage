using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// crop_seasons (F3.9): the store's debt-collection seasons. The phase is derived, not stored (D2); seasons chaining
// without gap or overlap is checked by the application under the store lock (D3). The optional seasonal_rule_id links
// the inventory seasonal rule of the same season for shared reporting.
internal sealed class CropSeasonConfiguration : EntityConfiguration<CropSeason>
{
    protected override string TableName => "crop_seasons";

    protected override void ConfigureEntity(EntityTypeBuilder<CropSeason> builder)
    {
        builder.Property(season => season.Code).HasMaxLength(30);
        builder.Property(season => season.Name).HasMaxLength(150);
        builder.Property(season => season.CropType).HasMaxLength(50).HasDbDefault("RICE");
        builder.Property(season => season.ClosedSummary).IsJson();
        builder.Property(season => season.Version).HasDbDefault(0L);
        builder.Ignore(season => season.IsClosed);

        builder.HasReference<CropSeason, Store>(season => season.StoreId);
        builder.HasReference<CropSeason, SeasonalRule>(season => season.SeasonalRuleId);
        builder.HasUserReference(season => season.ClosedBy);

        builder.HasIndex(season => new { season.StoreId, season.Code }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(season => new { season.StoreId, season.HarvestStart });

        builder.HasCheck("harvest_dates", "harvest_end > harvest_start");
        builder.HasCheck("order_window", "order_window_start < harvest_start");
        builder.HasCheck("grace_days", "grace_days BETWEEN 0 AND 365");
        builder.HasCheck("payment_due_date", "payment_due_date = harvest_end + grace_days");
        builder.HasCheck("closed", "(closed_at IS NULL AND closed_by IS NULL AND closed_summary IS NULL) "
            + "OR (closed_at IS NOT NULL AND closed_by IS NOT NULL AND closed_summary IS NOT NULL)");
    }
}
