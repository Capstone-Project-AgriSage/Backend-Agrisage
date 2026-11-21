using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiProviderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_provider_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    model_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    encrypted_api_key = table.Column<string>(type: "text", nullable: true),
                    api_key_hint = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_provider_settings", x => x.id);
                    table.CheckConstraint("ck_ai_provider_settings_enabled_needs_key", "is_enabled = FALSE OR encrypted_api_key IS NOT NULL");
                    table.CheckConstraint("ck_ai_provider_settings_provider", "provider IN ('ANTHROPIC', 'GOOGLE')");
                    table.CheckConstraint("ck_ai_provider_settings_timeout_seconds", "timeout_seconds BETWEEN 10 AND 300");
                    table.ForeignKey(
                        name: "fk_ai_provider_settings_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10001"), "AI_PROVIDER.READ", true, false, "AI_PROVIDER", "Xem nhà cung cấp AI" },
                    { new Guid("a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10002"), "AI_PROVIDER.MANAGE", true, false, "AI_PROVIDER", "Cấu hình nhà cung cấp AI" }
                });

            migrationBuilder.CreateIndex(
                name: "ux_ai_provider_settings_provider",
                table: "ai_provider_settings",
                column: "provider",
                unique: true,
                filter: "deleted_at IS NULL");

            // Only the administrator sees and changes the AI provider (AI_DIAGNOSIS.md 2.2); not delegable.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', 'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10001'::uuid),
                    ('ADMIN', 'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10002'::uuid)
                ) AS defaults(role_code, permission_id)
                JOIN roles r ON r.code = defaults.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM store_member_permissions WHERE permission_id IN (
                    'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10001'::uuid, 'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10002'::uuid);
                DELETE FROM role_permissions WHERE permission_id IN (
                    'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10001'::uuid, 'a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10002'::uuid);
                """);

            migrationBuilder.DropTable(
                name: "ai_provider_settings");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10001"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("a1c0b7e2-5d4f-4b3a-9e61-2c7d00a10002"));
        }
    }
}
