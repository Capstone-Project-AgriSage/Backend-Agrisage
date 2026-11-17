using AgriSage.Application.Features.Stocktakes;
using AgriSage.Domain.Features.Inventory;
using AgriSage.Domain.Features.Inventory.Entities;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Reviews StocktakeItemSnapshotMovementCount (database design §35.24): exact stale lines replace the 5-minute margin of
// §35.19. The back-fill gives existing lines the movement count that keeps the old rule, so a stocktake being counted
// while the migration is applied neither gains nor loses a stale line.
public class StocktakeMovementCountMigrationTests
{
    private const string Previous = "20261112151319_ContentContactPermissions";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Up_adds_the_count_backfills_it_then_makes_it_required_without_a_default_and_checked()
    {
        var up = new StocktakeItemSnapshotMovementCount().UpOperations;

        Assert.Collection(
            up,
            operation =>
            {
                var add = Assert.IsType<AddColumnOperation>(operation);
                Assert.Equal(("stocktake_items", "snapshot_movement_count", "integer", true),
                    (add.Table, add.Name, add.ColumnType, add.IsNullable));
                Assert.Null(add.DefaultValue);
                Assert.Null(add.DefaultValueSql);
            },
            operation =>
            {
                var sql = Assert.IsType<SqlOperation>(operation).Sql;
                Assert.StartsWith("UPDATE stocktake_items", sql.Trim());
                Assert.Contains("m.status IN ('POSTED', 'REVERSED')", sql);
                Assert.Contains("mi.deleted_at IS NULL", sql);
                Assert.Contains("m.posted_at <= i.snapshot_at - interval '5 minutes'", sql);
                Assert.Contains("WHERE i.snapshot_movement_count IS NULL", sql);
                Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
            },
            operation =>
            {
                var alter = Assert.IsType<AlterColumnOperation>(operation);
                Assert.Equal(("stocktake_items", "snapshot_movement_count", false), (alter.Table, alter.Name, alter.IsNullable));
                Assert.Null(alter.DefaultValue);
                Assert.Null(alter.DefaultValueSql);
            },
            operation =>
            {
                var check = Assert.IsType<AddCheckConstraintOperation>(operation);
                Assert.Equal(("stocktake_items", "ck_stocktake_items_snapshot_movement_count", "snapshot_movement_count >= 0"),
                    (check.Table, check.Name, check.Sql));
            });
    }

    [Fact]
    public void Down_only_drops_the_check_and_the_new_column()
    {
        Assert.Collection(
            new StocktakeItemSnapshotMovementCount().DownOperations,
            operation => Assert.Equal(("stocktake_items", "ck_stocktake_items_snapshot_movement_count"),
                (Assert.IsType<DropCheckConstraintOperation>(operation).Table, ((DropCheckConstraintOperation)operation).Name)),
            operation => Assert.Equal(("stocktake_items", "snapshot_movement_count"),
                (Assert.IsType<DropColumnOperation>(operation).Table, ((DropColumnOperation)operation).Name)));
    }

    // Real PostgreSQL in an isolated schema: back to the previous migration, lines and movements of every shape, then up.
    // Each line is stale after the migration exactly when the old rule (a POSTED movement in (snapshot_at − 5 min,
    // counted_at]) said so.
    [CreditDbFact]
    public async Task Backfill_keeps_the_old_stale_result_of_every_existing_line()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var actor = e.User.UserId!.Value;
        var s = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds());
        using (var scope = e.Scope())
            await scope.ServiceProvider.GetRequiredService<AgriSageDbContext>().GetService<IMigrator>().MigrateAsync(Previous, Token);

        // Movements on the lot: A posted at s−10 min, E posted at s−20 min then reversed, B at s−2 min (inside the old
        // margin), C at s+5 min, D a draft. Back-fill counts items with posted_at <= snapshot_at − 5 min.
        var stocktakes = new Guid[4];
        using (var scope = e.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            StockMovement Movement(string number, DateTimeOffset? postedAt, bool reverse = false)
            {
                var m = new StockMovement(e.StoreId, number, StockMovementType.AdjustmentIn, postedAt ?? s.AddMinutes(-30), actor);
                m.AddItem(e.LotId, new LotBalanceChange(1, 1, 1, 1001, 1001));
                if (postedAt is { } at) m.Post(actor, at);
                if (reverse) m.MarkReversed();
                return m;
            }
            db.StockMovements.AddRange(Movement("SM-A", s.AddMinutes(-10)), Movement("SM-E", s.AddMinutes(-20), reverse: true),
                Movement("SM-B", s.AddMinutes(-2)), Movement("SM-C", s.AddMinutes(5)), Movement("SM-D", null));
            for (var i = 0; i < stocktakes.Length; i++)
            {
                var stocktake = new Stocktake(e.StoreId, $"ST-M{i}", actor);
                db.Stocktakes.Add(stocktake);
                stocktakes[i] = stocktake.Id;
            }
            await db.SaveChangesAsync(Token);

            // One line per stocktake (one lot), written as the previous schema had them.
            (DateTimeOffset SnapshotAt, DateTimeOffset? CountedAt)[] lines =
                [(s, s.AddMinutes(10)), (s.AddMinutes(20), s.AddMinutes(21)), (s.AddMinutes(8), s.AddMinutes(9)), (s.AddMinutes(-1), null)];
            for (var i = 0; i < lines.Length; i++)
            {
                var counted = lines[i].CountedAt;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO stocktake_items (id, stocktake_id, inventory_lot_id, system_quantity_snapshot, snapshot_at,
                        counted_quantity, difference_quantity, counted_by, counted_at, created_at, updated_at)
                    VALUES ({Guid.NewGuid()}, {stocktakes[i]}, {e.LotId}, 1000, {lines[i].SnapshotAt},
                        {(counted is null ? null : 1000L)}, {(counted is null ? null : 0L)}, {(counted is null ? null : actor)},
                        {counted}, {s}, {s})
                    """, Token);
            }
        }

        using (var scope = e.Scope())
            await scope.ServiceProvider.GetRequiredService<AgriSageDbContext>().Database.MigrateAsync(Token);

        using (var scope = e.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var counts = await db.StocktakeItems.AsNoTracking().ToDictionaryAsync(i => i.StocktakeId, i => i.SnapshotMovementCount, Token);
            Assert.Equal([2, 4, 3, 2], stocktakes.Select(id => counts[id]));

            var queries = new StocktakeQueries(db);
            async Task<bool> Stale(Guid id) => (await queries.StaleItemIdsAsync(id, Token)).Count == 1;
            Assert.True(await Stale(stocktakes[0]));   // B and C fall in (s − 5, s + 10]
            Assert.False(await Stale(stocktakes[1]));  // nothing in (s + 15, s + 21]
            Assert.True(await Stale(stocktakes[2]));   // C falls in (s + 3, s + 9]
            Assert.False(await Stale(stocktakes[3]));  // not counted yet
        }
    }
}
