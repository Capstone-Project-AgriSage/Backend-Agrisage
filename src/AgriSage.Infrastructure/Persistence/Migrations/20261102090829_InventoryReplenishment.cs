using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryReplenishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lead_time_days",
                table: "store_products",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<bool>(
                name: "replenishment_enabled",
                table: "store_products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "safety_stock_base",
                table: "store_products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "target_coverage_days",
                table: "store_products",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<long>(
                name: "target_stock_level_base",
                table: "store_products",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_seasonal_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    crop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    region = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    prepare_days = table.Column<int>(type: "integer", nullable: false),
                    demand_factor = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_seasonal_rules", x => x.id);
                    table.CheckConstraint("ck_seasonal_rule_dates", "ends_on >= starts_on");
                    table.CheckConstraint("ck_seasonal_rule_factor", "demand_factor BETWEEN 0.1 AND 5");
                    table.CheckConstraint("ck_seasonal_rule_prepare", "prepare_days BETWEEN 0 AND 90");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_rules_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_rules_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_seasonal_rule_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    seasonal_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_stock_level_base = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_seasonal_rule_items", x => x.id);
                    table.CheckConstraint("ck_seasonal_item_target", "target_stock_level_base >= 0");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_rule_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_rule_items_seasonal_rule_id",
                        column: x => x.seasonal_rule_id,
                        principalTable: "inventory_seasonal_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_seasonal_rule_items_store_product_id",
                        column: x => x.store_product_id,
                        principalTable: "store_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d441"), "REPLENISHMENT.READ", true, true, "INVENTORY", "Xem kế hoạch nhập hàng" },
                    { new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d442"), "SEASONAL_RULES.READ", true, true, "INVENTORY", "Xem quy tắc mùa vụ" },
                    { new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d443"), "SEASONAL_RULES.MANAGE", true, true, "INVENTORY", "Quản lý quy tắc mùa vụ" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_store_product_replenishment",
                table: "store_products",
                sql: "(min_stock_level_base IS NULL OR min_stock_level_base >= 0) AND (target_stock_level_base IS NULL OR (target_stock_level_base >= 0 AND (min_stock_level_base IS NULL OR target_stock_level_base >= min_stock_level_base))) AND lead_time_days BETWEEN 0 AND 365 AND safety_stock_base >= 0 AND target_coverage_days BETWEEN 1 AND 90");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_rule_items_seasonal_rule_id",
                table: "inventory_seasonal_rule_items",
                column: "seasonal_rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_rule_items_store_product_id",
                table: "inventory_seasonal_rule_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_seasonal_rule_items_seasonal_rule_id_store_product",
                table: "inventory_seasonal_rule_items",
                columns: new[] { "seasonal_rule_id", "store_product_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_seasonal_rules_store_id_is_active_starts_on",
                table: "inventory_seasonal_rules",
                columns: new[] { "store_id", "is_active", "starts_on" });

            // Grant only new permission defaults; preserve every existing role/member decision.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, p.id, TRUE, now(), now()
                FROM roles r CROSS JOIN permissions p
                WHERE r.code IN ('ADMIN', 'STORE_OWNER') AND r.deleted_at IS NULL
                  AND p.code IN ('REPLENISHMENT.READ', 'SEASONAL_RULES.READ', 'SEASONAL_RULES.MANAGE')
                ON CONFLICT (role_id, permission_id) DO NOTHING;

                CREATE FUNCTION agrisage_capture_planning_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'UPDATE' AND NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
                  PERFORM agrisage_enqueue('inventory', 'store', NEW.store_id);
                  RETURN NEW;
                END $$;
                CREATE TRIGGER agrisage_planning_changed AFTER INSERT OR UPDATE ON inventory_seasonal_rules
                  FOR EACH ROW EXECUTE FUNCTION agrisage_capture_planning_change();
                CREATE TRIGGER agrisage_planning_changed AFTER UPDATE ON store_products
                  FOR EACH ROW EXECUTE FUNCTION agrisage_capture_planning_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS agrisage_planning_changed ON inventory_seasonal_rules;
                DROP TRIGGER IF EXISTS agrisage_planning_changed ON store_products;
                DROP FUNCTION agrisage_capture_planning_change();
                DELETE FROM store_member_permissions WHERE permission_id IN (
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d441'::uuid,
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d442'::uuid,
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d443'::uuid);
                DELETE FROM role_permissions WHERE permission_id IN (
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d441'::uuid,
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d442'::uuid,
                  'cb30e359-0710-4c8a-b2ab-7ea248e5d443'::uuid);
                """);
            migrationBuilder.DropTable(
                name: "inventory_seasonal_rule_items");

            migrationBuilder.DropTable(
                name: "inventory_seasonal_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_store_product_replenishment",
                table: "store_products");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d441"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d442"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cb30e359-0710-4c8a-b2ab-7ea248e5d443"));

            migrationBuilder.DropColumn(
                name: "lead_time_days",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "replenishment_enabled",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "safety_stock_base",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "target_coverage_days",
                table: "store_products");

            migrationBuilder.DropColumn(
                name: "target_stock_level_base",
                table: "store_products");
        }
    }
}
