using System.Text.Json;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Debt;

// store_debt_policies (F3.9): one row per store with the owner's debt settings and the documented defaults.
internal sealed class StoreDebtPolicyConfiguration : EntityConfiguration<StoreDebtPolicy>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override string TableName => "store_debt_policies";

    protected override void ConfigureEntity(EntityTypeBuilder<StoreDebtPolicy> builder)
    {
        builder.Property(policy => policy.DefaultGraceDays).HasDbDefault(30);
        // {"n2":-7,"n4":14,"n5":30} as in the policy document.
        builder.Property(policy => policy.ReminderOffsets).IsJson()
            .HasConversion(value => JsonSerializer.Serialize(value, Json),
                value => JsonSerializer.Deserialize<DebtReminderOffsets>(value, Json)!);
        builder.Property(policy => policy.StaffRolloverLimit).HasDbDefault(1);
        builder.Property(policy => policy.OwnerReviewAfterRollovers).HasDbDefault(2);
        builder.Property(policy => policy.OnTimeSeasonsForLimitRaise).HasDbDefault(2);
        builder.Property(policy => policy.MaxTotalExtensionDays).HasDbDefault(365);
        builder.Property(policy => policy.Version).HasDbDefault(0L);

        builder.HasReference<StoreDebtPolicy, Store>(policy => policy.StoreId);
        builder.HasIndex(policy => policy.StoreId, "ux_store_debt_policies_store_id")
            .HasDatabaseName("ux_store_debt_policies_store_id").IsUnique().HasFilter("deleted_at IS NULL");

        builder.HasCheck("default_grace_days", "default_grace_days BETWEEN 0 AND 365");
        builder.HasCheck("staff_rollover_limit", "staff_rollover_limit >= 0");
        builder.HasCheck("owner_review_after_rollovers", "owner_review_after_rollovers >= staff_rollover_limit");
        builder.HasCheck("on_time_seasons_for_limit_raise", "on_time_seasons_for_limit_raise >= 1");
        builder.HasCheck("max_credit_term_days", "max_credit_term_days IS NULL OR max_credit_term_days >= 1");
        builder.HasCheck("max_total_extension_days", "max_total_extension_days >= 1");
    }
}
