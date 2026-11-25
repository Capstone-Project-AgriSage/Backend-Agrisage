using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DebtFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "debt_follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    debt_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    contacted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    promised_date = table.Column<DateOnly>(type: "date", nullable: true),
                    promised_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    outstanding_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    overdue_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_follow_ups", x => x.id);
                    table.CheckConstraint("ck_debt_follow_ups_amounts", "outstanding_amount > 0 AND overdue_amount >= 0 AND overdue_amount <= outstanding_amount");
                    table.CheckConstraint("ck_debt_follow_ups_channel", "channel IN ('PHONE_CALL', 'VISIT', 'AT_STORE', 'MESSAGE', 'OTHER')");
                    table.CheckConstraint("ck_debt_follow_ups_outcome", "outcome IN ('PROMISED', 'ASKED_MORE_TIME', 'NO_ANSWER', 'REFUSED', 'OTHER')");
                    table.CheckConstraint("ck_debt_follow_ups_promise", "(outcome = 'PROMISED' AND promised_date IS NOT NULL) OR (outcome <> 'PROMISED' AND promised_date IS NULL AND promised_amount IS NULL)");
                    table.CheckConstraint("ck_debt_follow_ups_promised_amount", "promised_amount IS NULL OR promised_amount > 0");
                    table.ForeignKey(
                        name: "fk_debt_follow_ups_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debt_follow_ups_debt_account_id",
                        column: x => x.debt_account_id,
                        principalTable: "debt_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debt_follow_ups_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[] { new Guid("407a9c04-b966-47e1-b306-8e4b06fd0843"), "DEBT.FOLLOW_UP", true, true, "DEBT", "Ghi nhận nhắc nợ" });

            // Role defaults of the new permission (same shape as CropSeasonDebtPermissions): ADMIN, STORE_OWNER and SALES_STAFF.
            // Existing role/member decisions are kept; re-running is harmless.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', '407a9c04-b966-47e1-b306-8e4b06fd0843'::uuid),
                    ('STORE_OWNER', '407a9c04-b966-47e1-b306-8e4b06fd0843'::uuid),
                    ('SALES_STAFF', '407a9c04-b966-47e1-b306-8e4b06fd0843'::uuid)
                ) AS defaults(role_code, permission_id)
                JOIN roles r ON r.code = defaults.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_debt_follow_ups_debt_account_id_contacted_at",
                table: "debt_follow_ups",
                columns: new[] { "debt_account_id", "contacted_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debt_follow_ups");

            // role_permissions and store_member_permissions reference permissions with RESTRICT: remove their grants first.
            migrationBuilder.Sql("""
                DELETE FROM store_member_permissions WHERE permission_id = '407a9c04-b966-47e1-b306-8e4b06fd0843'::uuid;
                DELETE FROM role_permissions WHERE permission_id = '407a9c04-b966-47e1-b306-8e4b06fd0843'::uuid;
                """);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("407a9c04-b966-47e1-b306-8e4b06fd0843"));
        }
    }
}
