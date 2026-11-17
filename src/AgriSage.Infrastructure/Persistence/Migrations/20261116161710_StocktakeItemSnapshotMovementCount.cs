using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    // Exact stale stocktake lines (database design §35.24, replaces the 5-minute margin of §35.19). Existing lines get
    // the posted movement items of their Lot up to snapshot_at - 5 minutes, so a stocktake already being counted keeps
    // exactly the old rule until its lines are refreshed.
    /// <inheritdoc />
    public partial class StocktakeItemSnapshotMovementCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "snapshot_movement_count",
                table: "stocktake_items",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);

            migrationBuilder.AlterColumn<int>(
                name: "snapshot_movement_count",
                table: "stocktake_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stocktake_items_snapshot_movement_count",
                table: "stocktake_items",
                sql: "snapshot_movement_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stocktake_items_snapshot_movement_count",
                table: "stocktake_items");

            migrationBuilder.DropColumn(
                name: "snapshot_movement_count",
                table: "stocktake_items");
        }

        internal const string BackfillSql = """
            UPDATE stocktake_items AS i
            SET snapshot_movement_count = (
                SELECT count(*)
                FROM stock_movement_items AS mi
                JOIN stock_movements AS m ON m.id = mi.stock_movement_id
                WHERE mi.inventory_lot_id = i.inventory_lot_id
                  AND mi.deleted_at IS NULL
                  AND m.status IN ('POSTED', 'REVERSED')
                  AND m.posted_at <= i.snapshot_at - interval '5 minutes')
            WHERE i.snapshot_movement_count IS NULL;
            """;
    }
}
