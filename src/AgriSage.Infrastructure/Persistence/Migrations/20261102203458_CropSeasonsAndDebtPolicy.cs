using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CropSeasonsAndDebtPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_debt_entry_actions_action_type",
                table: "debt_entry_actions");

            migrationBuilder.AddColumn<Guid>(
                name: "crop_season_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "new_crop_season_id",
                table: "debt_entry_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "old_crop_season_id",
                table: "debt_entry_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "debt_entry_actions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "collection_crop_season_id",
                table: "debt_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "due_basis",
                table: "debt_entries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_crop_season_id",
                table: "debt_entries",
                type: "uuid",
                nullable: true);

            // Hand-edited: EF would fill existing rows with 0001-01-01. Existing debt keeps its current due date as its
            // original due date (no rollover has happened before this migration), then the column becomes NOT NULL.
            migrationBuilder.AddColumn<DateOnly>(
                name: "original_due_date",
                table: "debt_entries",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE debt_entries SET original_due_date = due_date WHERE original_due_date IS NULL;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "original_due_date",
                table: "debt_entries",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rollover_count",
                table: "debt_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "crop_seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    crop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RICE"),
                    order_window_start = table.Column<DateOnly>(type: "date", nullable: false),
                    harvest_start = table.Column<DateOnly>(type: "date", nullable: false),
                    harvest_end = table.Column<DateOnly>(type: "date", nullable: false),
                    grace_days = table.Column<int>(type: "integer", nullable: false),
                    payment_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    seasonal_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_summary = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crop_seasons", x => x.id);
                    table.CheckConstraint("ck_crop_seasons_closed", "(closed_at IS NULL AND closed_by IS NULL AND closed_summary IS NULL) OR (closed_at IS NOT NULL AND closed_by IS NOT NULL AND closed_summary IS NOT NULL)");
                    table.CheckConstraint("ck_crop_seasons_grace_days", "grace_days BETWEEN 0 AND 365");
                    table.CheckConstraint("ck_crop_seasons_harvest_dates", "harvest_end > harvest_start");
                    table.CheckConstraint("ck_crop_seasons_order_window", "order_window_start < harvest_start");
                    table.CheckConstraint("ck_crop_seasons_payment_due_date", "payment_due_date = harvest_end + grace_days");
                    table.ForeignKey(
                        name: "fk_crop_seasons_closed_by",
                        column: x => x.closed_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_crop_seasons_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_crop_seasons_seasonal_rule_id",
                        column: x => x.seasonal_rule_id,
                        principalTable: "inventory_seasonal_rules",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_crop_seasons_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_debt_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_grace_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    reminder_offsets = table.Column<string>(type: "jsonb", nullable: false),
                    staff_rollover_limit = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    owner_review_after_rollovers = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    on_time_seasons_for_limit_raise = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    max_credit_term_days = table.Column<int>(type: "integer", nullable: true),
                    max_total_extension_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 365),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_debt_policies", x => x.id);
                    table.CheckConstraint("ck_store_debt_policies_default_grace_days", "default_grace_days BETWEEN 0 AND 365");
                    table.CheckConstraint("ck_store_debt_policies_max_credit_term_days", "max_credit_term_days IS NULL OR max_credit_term_days >= 1");
                    table.CheckConstraint("ck_store_debt_policies_max_total_extension_days", "max_total_extension_days >= 1");
                    table.CheckConstraint("ck_store_debt_policies_on_time_seasons_for_limit_raise", "on_time_seasons_for_limit_raise >= 1");
                    table.CheckConstraint("ck_store_debt_policies_owner_review_after_rollovers", "owner_review_after_rollovers >= staff_rollover_limit");
                    table.CheckConstraint("ck_store_debt_policies_staff_rollover_limit", "staff_rollover_limit >= 0");
                    table.ForeignKey(
                        name: "fk_store_debt_policies_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_store_debt_policies_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_crop_season_id",
                table: "orders",
                column: "crop_season_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_crop_season_credit_only",
                table: "orders",
                sql: "crop_season_id IS NULL OR settlement_type = 'CREDIT'");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entry_actions_new_crop_season_id",
                table: "debt_entry_actions",
                column: "new_crop_season_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entry_actions_old_crop_season_id",
                table: "debt_entry_actions",
                column: "old_crop_season_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_debt_entry_actions_action_type",
                table: "debt_entry_actions",
                sql: "action_type IN ('DISPUTE', 'KEEP', 'ADJUST', 'CANCEL', 'CHANGE_DUE_DATE', 'ROLLOVER')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_debt_entry_actions_reason_code",
                table: "debt_entry_actions",
                sql: "reason_code IN ('MAT_MUA', 'THIEN_TAI', 'KHO_KHAN', 'TRI_AN', 'SAI_SOT', 'KHONG_THU_HOI', 'KHAC')");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_collection_crop_season_id",
                table: "debt_entries",
                column: "collection_crop_season_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_origin_crop_season_id",
                table: "debt_entries",
                column: "origin_crop_season_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_debt_entries_rollover_count",
                table: "debt_entries",
                sql: "rollover_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_crop_seasons_seasonal_rule_id",
                table: "crop_seasons",
                column: "seasonal_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_crop_seasons_store_id_harvest_start",
                table: "crop_seasons",
                columns: new[] { "store_id", "harvest_start" });

            migrationBuilder.CreateIndex(
                name: "ux_crop_seasons_store_id_code",
                table: "crop_seasons",
                columns: new[] { "store_id", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_store_debt_policies_store_id",
                table: "store_debt_policies",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_store_debt_policies_store_id",
                table: "store_debt_policies",
                column: "store_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_collection_crop_season_id",
                table: "debt_entries",
                column: "collection_crop_season_id",
                principalTable: "crop_seasons",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_origin_crop_season_id",
                table: "debt_entries",
                column: "origin_crop_season_id",
                principalTable: "crop_seasons",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entry_actions_new_crop_season_id",
                table: "debt_entry_actions",
                column: "new_crop_season_id",
                principalTable: "crop_seasons",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entry_actions_old_crop_season_id",
                table: "debt_entry_actions",
                column: "old_crop_season_id",
                principalTable: "crop_seasons",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_crop_season_id",
                table: "orders",
                column: "crop_season_id",
                principalTable: "crop_seasons",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: a rollback must not silently erase seasons, policies, rollovers or reason codes that the store
            // already recorded. Refuse it until an operator has dealt with that data.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM crop_seasons) OR EXISTS (SELECT 1 FROM store_debt_policies)
                       OR EXISTS (SELECT 1 FROM debt_entry_actions WHERE action_type = 'ROLLOVER' OR reason_code IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM debt_entries WHERE due_basis IS NOT NULL OR rollover_count > 0) THEN
                        RAISE EXCEPTION 'Cannot roll back CropSeasonsAndDebtPolicy while crop-season debt data exists.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_debt_entries_collection_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_debt_entries_origin_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_debt_entry_actions_new_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropForeignKey(
                name: "fk_debt_entry_actions_old_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_crop_season_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "crop_seasons");

            migrationBuilder.DropTable(
                name: "store_debt_policies");

            migrationBuilder.DropIndex(
                name: "ix_orders_crop_season_id",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_crop_season_credit_only",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_debt_entry_actions_new_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropIndex(
                name: "ix_debt_entry_actions_old_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_debt_entry_actions_action_type",
                table: "debt_entry_actions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_debt_entry_actions_reason_code",
                table: "debt_entry_actions");

            migrationBuilder.DropIndex(
                name: "ix_debt_entries_collection_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropIndex(
                name: "ix_debt_entries_origin_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_debt_entries_rollover_count",
                table: "debt_entries");

            migrationBuilder.DropColumn(
                name: "crop_season_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "new_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropColumn(
                name: "old_crop_season_id",
                table: "debt_entry_actions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "debt_entry_actions");

            migrationBuilder.DropColumn(
                name: "collection_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropColumn(
                name: "due_basis",
                table: "debt_entries");

            migrationBuilder.DropColumn(
                name: "origin_crop_season_id",
                table: "debt_entries");

            migrationBuilder.DropColumn(
                name: "original_due_date",
                table: "debt_entries");

            migrationBuilder.DropColumn(
                name: "rollover_count",
                table: "debt_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_debt_entry_actions_action_type",
                table: "debt_entry_actions",
                sql: "action_type IN ('DISPUTE', 'KEEP', 'ADJUST', 'CANCEL', 'CHANGE_DUE_DATE')");
        }
    }
}
