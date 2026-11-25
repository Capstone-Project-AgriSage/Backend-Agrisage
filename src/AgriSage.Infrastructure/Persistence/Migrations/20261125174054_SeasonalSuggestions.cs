using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonalSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_seasonal_suggestion_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    crop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    region = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    prepare_days = table.Column<int>(type: "integer", nullable: false),
                    compared_from = table.Column<DateOnly>(type: "date", nullable: false),
                    compared_to = table.Column<DateOnly>(type: "date", nullable: false),
                    seasonal_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    crop_season_id = table.Column<Guid>(type: "uuid", nullable: true),
                    candidate_count = table.Column<int>(type: "integer", nullable: false),
                    candidates = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_seasonal_suggestion_runs", x => x.id);
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_runs_candidate_count", "candidate_count >= 0");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_runs_compared_dates", "compared_to >= compared_from");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_runs_dates", "ends_on >= starts_on");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_runs_prepare_days", "prepare_days BETWEEN 0 AND 90");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_runs_crop_season_id",
                        column: x => x.crop_season_id,
                        principalTable: "crop_seasons",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_runs_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_runs_seasonal_rule_id",
                        column: x => x.seasonal_rule_id,
                        principalTable: "inventory_seasonal_rules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_runs_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_seasonal_suggestion_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    product_group = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    sold_last_season_base = table.Column<long>(type: "bigint", nullable: true),
                    sellable_base = table.Column<long>(type: "bigint", nullable: true),
                    suggested_target_base = table.Column<long>(type: "bigint", nullable: true),
                    order_by_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_seasonal_suggestion_items", x => x.id);
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_items_confidence", "confidence IN ('HIGH', 'MEDIUM', 'LOW')");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_items_product_group", "product_group IN ('FERTILIZER', 'CROP_PROTECTION', 'SEED', 'OTHER')");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_items_rank", "\"rank\" > 0");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_items_sold_last_season_base", "sold_last_season_base IS NULL OR sold_last_season_base >= 0");
                    table.CheckConstraint("ck_inventory_seasonal_suggestion_items_suggested_target_base", "suggested_target_base IS NULL OR suggested_target_base >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_items_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_items_run_id",
                        column: x => x.run_id,
                        principalTable: "inventory_seasonal_suggestion_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_suggestion_items_store_product_id",
                        column: x => x.store_product_id,
                        principalTable: "store_products",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_items_product_id",
                table: "inventory_seasonal_suggestion_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_items_run_id",
                table: "inventory_seasonal_suggestion_items",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_items_store_product_id",
                table: "inventory_seasonal_suggestion_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_seasonal_suggestion_items_run_id_product_id",
                table: "inventory_seasonal_suggestion_items",
                columns: new[] { "run_id", "product_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_seasonal_suggestion_items_run_id_rank",
                table: "inventory_seasonal_suggestion_items",
                columns: new[] { "run_id", "rank" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_runs_crop_season_id",
                table: "inventory_seasonal_suggestion_runs",
                column: "crop_season_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_runs_seasonal_rule_id",
                table: "inventory_seasonal_suggestion_runs",
                column: "seasonal_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_suggestion_runs_store_id_starts_on",
                table: "inventory_seasonal_suggestion_runs",
                columns: new[] { "store_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_seasonal_suggestion_runs_store_id_schedule_key",
                table: "inventory_seasonal_suggestion_runs",
                columns: new[] { "store_id", "schedule_key" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_seasonal_suggestion_items");

            migrationBuilder.DropTable(
                name: "inventory_seasonal_suggestion_runs");
        }
    }
}
