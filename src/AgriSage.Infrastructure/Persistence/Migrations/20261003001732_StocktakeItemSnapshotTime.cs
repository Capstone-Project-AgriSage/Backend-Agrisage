using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StocktakeItemSnapshotTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database design §35.19. Three steps instead of EF's NOT NULL + year-0001 default: existing lines take
            // their creation time (when the snapshot was taken), and no default is left on the column.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "snapshot_at",
                table: "stocktake_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE stocktake_items SET snapshot_at = created_at WHERE snapshot_at IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "snapshot_at",
                table: "stocktake_items",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "snapshot_at",
                table: "stocktake_items");
        }
    }
}
