using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_refund_method",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.CreateTable(
                name: "sales_exchanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    replacement_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    replacement_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_exchanges", x => x.id);
                    table.CheckConstraint("ck_sales_exchanges_amount", "replacement_amount >= 0");
                    table.CheckConstraint("ck_sales_exchanges_completion", "(status = 'COMPLETED' AND replacement_order_id IS NOT NULL AND completed_at IS NOT NULL AND settlement_reference IS NOT NULL AND difference IS NOT NULL) OR (status <> 'COMPLETED' AND replacement_order_id IS NULL AND completed_at IS NULL)");
                    table.CheckConstraint("ck_sales_exchanges_order", "replacement_order_id IS NULL OR replacement_order_id <> order_id");
                    table.CheckConstraint("ck_sales_exchanges_status", "status IN ('REQUESTED', 'APPROVED', 'RECEIVED', 'READY_TO_COMPLETE', 'COMPLETED', 'REJECTED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_sales_exchanges_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_exchanges_farmer_profile_id",
                        column: x => x.farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_exchanges_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_exchanges_replacement_order_id",
                        column: x => x.replacement_order_id,
                        principalTable: "orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_exchanges_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_exchanges_sales_return_id",
                        column: x => x.sales_return_id,
                        principalTable: "sales_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_exchanges_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    exchange_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_packaging_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    packaging_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_items", x => x.id);
                    table.CheckConstraint("ck_exchange_items_amount", "unit_price >= 0 AND line_total = unit_price * quantity");
                    table.CheckConstraint("ck_exchange_items_quantity", "quantity > 0 AND base_quantity > 0");
                    table.ForeignKey(
                        name: "fk_exchange_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_exchange_items_exchange_id",
                        column: x => x.exchange_id,
                        principalTable: "sales_exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_items_product_packaging_id",
                        column: x => x.product_packaging_id,
                        principalTable: "product_packagings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exchange_items_store_product_id",
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
                    { new Guid("06ca7d3d-3b93-4ba3-92ad-078082db8aa4"), "EXCHANGES.REJECT", true, true, "EXCHANGES", "Từ chối đổi hàng" },
                    { new Guid("12fe7285-dc25-4cb8-9e23-22329f3f73b1"), "EXCHANGES.CREATE", true, true, "EXCHANGES", "Tạo đổi hàng" },
                    { new Guid("17bd2b0f-1a21-4fe3-91df-a284b9312dd9"), "MY_EXCHANGES.CANCEL", true, false, "MY_EXCHANGES", "Hủy đổi hàng" },
                    { new Guid("266e6358-c9f3-4346-bbe4-44e8d2c27a6f"), "EXCHANGES.COMPLETE", true, true, "EXCHANGES", "Quyết toán đổi hàng" },
                    { new Guid("5d3fab42-03d4-46f7-b72d-0db842d3aa64"), "EXCHANGES.READ", true, true, "EXCHANGES", "Xem đổi hàng" },
                    { new Guid("66eae31e-c328-46c1-a7c8-9d0dd7bd287f"), "EXCHANGES.COMPLETE_INSPECTION", true, true, "EXCHANGES", "Chốt kiểm tra đổi hàng" },
                    { new Guid("7e54b1e8-fd9b-491c-be4e-fb0e9f7c5120"), "MY_EXCHANGES.CREATE", true, false, "MY_EXCHANGES", "Tạo đổi hàng" },
                    { new Guid("962b4361-3d3c-4afa-865a-700bebb731f7"), "MY_EXCHANGES.READ", true, false, "MY_EXCHANGES", "Xem đổi hàng" },
                    { new Guid("b4752081-dbbd-4313-bf47-52935cb876d2"), "EXCHANGES.CANCEL", true, true, "EXCHANGES", "Hủy đổi hàng" },
                    { new Guid("cb6aed27-e9c8-4696-b6a6-4c0710d2bcd7"), "EXCHANGES.RECEIVE", true, true, "EXCHANGES", "Thu hồi hàng đổi" },
                    { new Guid("d07557fa-e892-41c4-8bb6-44f73bed34a5"), "EXCHANGES.INSPECT", true, true, "EXCHANGES", "Kiểm tra hàng đổi" },
                    { new Guid("e723a6be-8da2-4dd9-ab0c-87bc5f33cfd0"), "EXCHANGES.APPROVE", true, true, "EXCHANGES", "Duyệt đổi hàng" }
                });

            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, v.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', '5d3fab42-03d4-46f7-b72d-0db842d3aa64'::uuid),
                    ('STORE_OWNER', '5d3fab42-03d4-46f7-b72d-0db842d3aa64'::uuid),
                    ('SALES_STAFF', '5d3fab42-03d4-46f7-b72d-0db842d3aa64'::uuid),
                    ('ADMIN', '12fe7285-dc25-4cb8-9e23-22329f3f73b1'::uuid),
                    ('STORE_OWNER', '12fe7285-dc25-4cb8-9e23-22329f3f73b1'::uuid),
                    ('SALES_STAFF', '12fe7285-dc25-4cb8-9e23-22329f3f73b1'::uuid),
                    ('ADMIN', 'e723a6be-8da2-4dd9-ab0c-87bc5f33cfd0'::uuid),
                    ('STORE_OWNER', 'e723a6be-8da2-4dd9-ab0c-87bc5f33cfd0'::uuid),
                    ('ADMIN', '06ca7d3d-3b93-4ba3-92ad-078082db8aa4'::uuid),
                    ('STORE_OWNER', '06ca7d3d-3b93-4ba3-92ad-078082db8aa4'::uuid),
                    ('ADMIN', 'b4752081-dbbd-4313-bf47-52935cb876d2'::uuid),
                    ('STORE_OWNER', 'b4752081-dbbd-4313-bf47-52935cb876d2'::uuid),
                    ('SALES_STAFF', 'b4752081-dbbd-4313-bf47-52935cb876d2'::uuid),
                    ('ADMIN', 'cb6aed27-e9c8-4696-b6a6-4c0710d2bcd7'::uuid),
                    ('STORE_OWNER', 'cb6aed27-e9c8-4696-b6a6-4c0710d2bcd7'::uuid),
                    ('SALES_STAFF', 'cb6aed27-e9c8-4696-b6a6-4c0710d2bcd7'::uuid),
                    ('ADMIN', 'd07557fa-e892-41c4-8bb6-44f73bed34a5'::uuid),
                    ('STORE_OWNER', 'd07557fa-e892-41c4-8bb6-44f73bed34a5'::uuid),
                    ('SALES_STAFF', 'd07557fa-e892-41c4-8bb6-44f73bed34a5'::uuid),
                    ('ADMIN', '66eae31e-c328-46c1-a7c8-9d0dd7bd287f'::uuid),
                    ('STORE_OWNER', '66eae31e-c328-46c1-a7c8-9d0dd7bd287f'::uuid),
                    ('ADMIN', '266e6358-c9f3-4346-bbe4-44e8d2c27a6f'::uuid),
                    ('STORE_OWNER', '266e6358-c9f3-4346-bbe4-44e8d2c27a6f'::uuid),
                    ('FARMER', '962b4361-3d3c-4afa-865a-700bebb731f7'::uuid),
                    ('FARMER', '7e54b1e8-fd9b-491c-be4e-fb0e9f7c5120'::uuid),
                    ('FARMER', '17bd2b0f-1a21-4fe3-91df-a284b9312dd9'::uuid)
                ) AS v(role_code, permission_id)
                JOIN roles r ON r.code = v.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_refund_method",
                table: "refunds",
                sql: "refund_method IN ('CASH', 'BANK_TRANSFER', 'OTHER_EXTERNAL', 'EXCHANGE_CREDIT')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER', 'EXCHANGE_CREDIT')");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_items_product_packaging_id",
                table: "exchange_items",
                column: "product_packaging_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_items_store_product_id",
                table: "exchange_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_exchange_items_exchange_id_store_product_id_product_packagin",
                table: "exchange_items",
                columns: new[] { "exchange_id", "store_product_id", "product_packaging_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_exchanges_farmer_profile_id",
                table: "sales_exchanges",
                column: "farmer_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_exchanges_order_id",
                table: "sales_exchanges",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_exchanges_store_id_status_created_at",
                table: "sales_exchanges",
                columns: new[] { "store_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_sales_exchanges_replacement_order_id",
                table: "sales_exchanges",
                column: "replacement_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_exchanges_sales_return_id",
                table: "sales_exchanges",
                column: "sales_return_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_exchanges_store_id_requested_by_request_key",
                table: "sales_exchanges",
                columns: new[] { "store_id", "requested_by", "request_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM sales_exchanges)
                       OR EXISTS (SELECT 1 FROM payments WHERE payment_method = 'EXCHANGE_CREDIT')
                       OR EXISTS (SELECT 1 FROM refunds WHERE refund_method = 'EXCHANGE_CREDIT') THEN
                        RAISE EXCEPTION 'Cannot downgrade after exchanges have been recorded; preserve exchange and financial records.';
                    END IF;
                END $$;
                DELETE FROM store_member_permissions WHERE permission_id IN (SELECT id FROM permissions WHERE module IN ('EXCHANGES', 'MY_EXCHANGES'));
                DELETE FROM role_permissions WHERE permission_id IN (SELECT id FROM permissions WHERE module IN ('EXCHANGES', 'MY_EXCHANGES'));
                """);

            migrationBuilder.DropTable(
                name: "exchange_items");

            migrationBuilder.DropTable(
                name: "sales_exchanges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refunds_refund_method",
                table: "refunds");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("06ca7d3d-3b93-4ba3-92ad-078082db8aa4"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("12fe7285-dc25-4cb8-9e23-22329f3f73b1"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("17bd2b0f-1a21-4fe3-91df-a284b9312dd9"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("266e6358-c9f3-4346-bbe4-44e8d2c27a6f"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5d3fab42-03d4-46f7-b72d-0db842d3aa64"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("66eae31e-c328-46c1-a7c8-9d0dd7bd287f"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("7e54b1e8-fd9b-491c-be4e-fb0e9f7c5120"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("962b4361-3d3c-4afa-865a-700bebb731f7"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("b4752081-dbbd-4313-bf47-52935cb876d2"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cb6aed27-e9c8-4696-b6a6-4c0710d2bcd7"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("d07557fa-e892-41c4-8bb6-44f73bed34a5"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("e723a6be-8da2-4dd9-ab0c-87bc5f33cfd0"));

            migrationBuilder.AddCheckConstraint(
                name: "ck_refunds_refund_method",
                table: "refunds",
                sql: "refund_method IN ('CASH', 'BANK_TRANSFER', 'OTHER_EXTERNAL')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_payment_method",
                table: "payments",
                sql: "payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER')");
        }
    }
}
