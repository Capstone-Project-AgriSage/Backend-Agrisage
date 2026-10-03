using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Reviews StocktakeItemSnapshotTime without a database (decision C-D6, database design §35.19).
public class StocktakeSnapshotMigrationTests
{
    [Fact]
    public void Up_adds_snapshot_at_backfills_it_from_created_at_then_makes_it_required_without_a_default()
    {
        var up = new StocktakeItemSnapshotTime().UpOperations;

        Assert.Collection(
            up,
            operation =>
            {
                var add = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal(("stocktake_items", "snapshot_at", true), (add.Table, add.Name, add.IsNullable));
                Assert.Null(add.DefaultValue);
                Assert.Null(add.DefaultValueSql);
            },
            operation => Assert.Equal(
                "UPDATE stocktake_items SET snapshot_at = created_at WHERE snapshot_at IS NULL;",
                Assert.IsType<SqlOperation>(operation).Sql),
            operation =>
            {
                var alter = Assert.IsType<AlterColumnOperation>(operation);
                Assert.Equal(("stocktake_items", "snapshot_at", false), (alter.Table, alter.Name, alter.IsNullable));
                Assert.Null(alter.DefaultValue);
                Assert.Null(alter.DefaultValueSql);
            });
    }

    [Fact]
    public void Down_only_drops_the_new_column()
    {
        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(new StocktakeItemSnapshotTime().DownOperations));

        Assert.Equal(("stocktake_items", "snapshot_at"), (drop.Table, drop.Name));
    }
}
