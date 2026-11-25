using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// DATABASE_DESIGN §35.25 (F4.7): two new tables for seasonal product suggestions and nothing else — no existing table,
// column or permission changes (SEASONAL_RULES.READ/MANAGE are reused).
public class SeasonalSuggestionsMigrationTests
{
    [Fact]
    public void Up_only_creates_the_run_and_item_tables_and_their_indexes()
    {
        var migration = new SeasonalSuggestions();
        Assert.All(migration.UpOperations, o => Assert.True(o is CreateTableOperation or CreateIndexOperation,
            $"Unexpected operation {o.GetType().Name}"));

        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToList();
        Assert.Equal(["inventory_seasonal_suggestion_runs", "inventory_seasonal_suggestion_items"], tables.Select(t => t.Name));
        Assert.All(tables, table => Assert.All(table.ForeignKeys, fk => Assert.True(
            fk.OnDelete is ReferentialAction.Restrict or ReferentialAction.NoAction, $"{fk.Name} deletes {fk.OnDelete}")));

        var runs = tables[0];
        Assert.Equal(
            ["id", "store_id", "schedule_key", "crop_type", "region", "starts_on", "ends_on", "prepare_days",
             "compared_from", "compared_to", "seasonal_rule_id", "crop_season_id", "candidate_count", "candidates",
             "created_at", "updated_at", "deleted_at", "deleted_by"],
            runs.Columns.Select(c => c.Name));
        Assert.Equal("jsonb", runs.Columns.Single(c => c.Name == "candidates").ColumnType);
        Assert.False(runs.Columns.Single(c => c.Name == "schedule_key").IsNullable);
        foreach (var check in new[] { "dates", "compared_dates", "prepare_days", "candidate_count" })
            Assert.Contains(runs.CheckConstraints, c => c.Name == $"ck_inventory_seasonal_suggestion_runs_{check}");
        Assert.Contains(runs.ForeignKeys, fk => fk.PrincipalTable == "stores" && fk.OnDelete == ReferentialAction.Restrict);
        Assert.Single(runs.ForeignKeys, fk => fk.PrincipalTable == "inventory_seasonal_rules");
        Assert.Contains(runs.ForeignKeys, fk => fk.PrincipalTable == "crop_seasons");

        var items = tables[1];
        Assert.Equal(
            ["id", "run_id", "product_id", "store_product_id", "rank", "product_group", "confidence", "reason", "evidence",
             "sold_last_season_base", "sellable_base", "suggested_target_base", "order_by_date", "created_at", "updated_at",
             "deleted_at", "deleted_by"],
            items.Columns.Select(c => c.Name));
        Assert.Equal(500, items.Columns.Single(c => c.Name == "reason").MaxLength);
        Assert.Contains(items.CheckConstraints, c => c.Name == "ck_inventory_seasonal_suggestion_items_product_group"
            && c.Sql == "product_group IN ('FERTILIZER', 'CROP_PROTECTION', 'SEED', 'OTHER')");
        foreach (var check in new[] { "confidence", "rank", "sold_last_season_base", "suggested_target_base" })
            Assert.Contains(items.CheckConstraints, c => c.Name == $"ck_inventory_seasonal_suggestion_items_{check}");
        Assert.Contains(items.ForeignKeys, fk => fk.PrincipalTable == "inventory_seasonal_suggestion_runs"
            && fk.OnDelete == ReferentialAction.Restrict);
        Assert.Contains(items.ForeignKeys, fk => fk.PrincipalTable == "products" && fk.OnDelete == ReferentialAction.Restrict);

        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToList();
        var schedule = Assert.Single(indexes, i => i.Name == "ux_inventory_seasonal_suggestion_runs_store_id_schedule_key");
        Assert.True(schedule.IsUnique);
        Assert.Equal("deleted_at IS NULL", schedule.Filter);
        Assert.Contains(indexes, i => i.Columns.SequenceEqual(["store_id", "starts_on"]));
        Assert.Contains(indexes, i => i.IsUnique && i.Columns.SequenceEqual(["run_id", "product_id"]) && i.Filter == "deleted_at IS NULL");
        Assert.Contains(indexes, i => i.IsUnique && i.Columns.SequenceEqual(["run_id", "rank"]) && i.Filter == "deleted_at IS NULL");
    }

    [Fact]
    public void Down_drops_the_items_before_the_runs()
    {
        var operations = new SeasonalSuggestions().DownOperations;
        Assert.Equal(["inventory_seasonal_suggestion_items", "inventory_seasonal_suggestion_runs"],
            operations.Select(o => Assert.IsType<DropTableOperation>(o).Name));
    }
}
