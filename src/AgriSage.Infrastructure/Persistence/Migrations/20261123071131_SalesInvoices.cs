using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    customer_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    pdf_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoices", x => x.id);
                    table.CheckConstraint("ck_sales_invoices_pdf_content", "octet_length(pdf_content) > 0");
                    table.CheckConstraint("ck_sales_invoices_snapshot_json", "jsonb_typeof(snapshot_json) = 'object'");
                    table.CheckConstraint("ck_sales_invoices_total_amount", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_sales_invoices_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_emails", x => x.id);
                    table.CheckConstraint("ck_invoice_emails_sent_at", "(status = 'SENT' AND sent_at IS NOT NULL) OR (status <> 'SENT' AND sent_at IS NULL)");
                    table.CheckConstraint("ck_invoice_emails_status", "status IN ('SENDING','SENT','FAILED','UNKNOWN')");
                    table.ForeignKey(
                        name: "fk_invoice_emails_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_emails_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("4a7f7e2c-54f2-4b89-96b0-172311230001"), "INVOICES.READ", true, true, "INVOICES", "Xem và tải hóa đơn" },
                    { new Guid("4a7f7e2c-54f2-4b89-96b0-172311230002"), "INVOICES.CREATE", true, true, "INVOICES", "Tạo hóa đơn từ đơn hàng" },
                    { new Guid("4a7f7e2c-54f2-4b89-96b0-172311230003"), "INVOICES.SEND_EMAIL", true, true, "INVOICES", "Gửi hóa đơn qua email" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_emails_invoice_id_created_at",
                table: "invoice_emails",
                columns: new[] { "invoice_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, p.id, TRUE, now(), now()
                FROM roles r CROSS JOIN permissions p
                WHERE r.code IN ('ADMIN', 'STORE_OWNER', 'SALES_STAFF') AND r.deleted_at IS NULL
                  AND p.code IN ('INVOICES.READ', 'INVOICES.CREATE', 'INVOICES.SEND_EMAIL')
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_invoice_emails_invoice_id_request_id",
                table: "invoice_emails",
                columns: new[] { "invoice_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_store_id_issued_at",
                table: "sales_invoices",
                columns: new[] { "store_id", "issued_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_sales_invoices_order_id",
                table: "sales_invoices",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_invoices_store_id_invoice_number",
                table: "sales_invoices",
                columns: new[] { "store_id", "invoice_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM store_member_permissions WHERE permission_id IN (SELECT id FROM permissions WHERE module = 'INVOICES');
                DELETE FROM role_permissions WHERE permission_id IN (SELECT id FROM permissions WHERE module = 'INVOICES');
                """);
            migrationBuilder.DropTable(
                name: "invoice_emails");

            migrationBuilder.DropTable(
                name: "sales_invoices");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("4a7f7e2c-54f2-4b89-96b0-172311230001"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("4a7f7e2c-54f2-4b89-96b0-172311230002"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("4a7f7e2c-54f2-4b89-96b0-172311230003"));
        }
    }
}
