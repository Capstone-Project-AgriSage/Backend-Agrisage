using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CropSeasonDebtPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("2ad0e01f-1a01-41ad-9fa3-3269d15755da"), "DEBT_POLICY.READ", true, true, "DEBT_POLICY", "Xem" },
                    { new Guid("5a85c6fb-065e-4d96-8e65-11d7ed536e66"), "CROP_SEASONS.MANAGE", true, false, "CROP_SEASONS", "Quản lý vụ mùa" },
                    { new Guid("5c2f2e3b-4b85-4ee0-b855-0aad4290088f"), "DEBT.ROLLOVER", true, true, "DEBT", "Gia hạn nợ" },
                    { new Guid("7ceb1441-6396-4245-8ab8-020712168942"), "CROP_SEASONS.READ", true, true, "CROP_SEASONS", "Xem" },
                    { new Guid("9ae57c49-af20-440b-a94f-9a33344d012c"), "DEBT_POLICY.MANAGE", true, false, "DEBT_POLICY", "Quản lý chính sách công nợ" },
                    { new Guid("ffbb2c76-2185-48a3-8e1e-200b47d47652"), "MY_CROP_SEASONS.READ", true, false, "MY_CROP_SEASONS", "Xem" }
                });

            // Role defaults of the new permissions (same shape as AdvisoryProblemProducts): ADMIN gets all of them, every
            // other role the ones its catalog entry lists. Existing role/member decisions are kept; re-running is harmless.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', '7ceb1441-6396-4245-8ab8-020712168942'::uuid),
                    ('SALES_STAFF', '7ceb1441-6396-4245-8ab8-020712168942'::uuid),
                    ('STORE_OWNER', '7ceb1441-6396-4245-8ab8-020712168942'::uuid),
                    ('ADMIN', '5a85c6fb-065e-4d96-8e65-11d7ed536e66'::uuid),
                    ('STORE_OWNER', '5a85c6fb-065e-4d96-8e65-11d7ed536e66'::uuid),
                    ('ADMIN', '2ad0e01f-1a01-41ad-9fa3-3269d15755da'::uuid),
                    ('SALES_STAFF', '2ad0e01f-1a01-41ad-9fa3-3269d15755da'::uuid),
                    ('STORE_OWNER', '2ad0e01f-1a01-41ad-9fa3-3269d15755da'::uuid),
                    ('ADMIN', '9ae57c49-af20-440b-a94f-9a33344d012c'::uuid),
                    ('STORE_OWNER', '9ae57c49-af20-440b-a94f-9a33344d012c'::uuid),
                    ('ADMIN', '5c2f2e3b-4b85-4ee0-b855-0aad4290088f'::uuid),
                    ('SALES_STAFF', '5c2f2e3b-4b85-4ee0-b855-0aad4290088f'::uuid),
                    ('STORE_OWNER', '5c2f2e3b-4b85-4ee0-b855-0aad4290088f'::uuid),
                    ('ADMIN', 'ffbb2c76-2185-48a3-8e1e-200b47d47652'::uuid),
                    ('FARMER', 'ffbb2c76-2185-48a3-8e1e-200b47d47652'::uuid)
                ) AS defaults(role_code, permission_id)
                JOIN roles r ON r.code = defaults.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // role_permissions and store_member_permissions reference permissions with RESTRICT: remove their grants first.
            migrationBuilder.Sql("""
                DELETE FROM store_member_permissions WHERE permission_id IN (
                    '7ceb1441-6396-4245-8ab8-020712168942'::uuid, '5a85c6fb-065e-4d96-8e65-11d7ed536e66'::uuid,
                    '2ad0e01f-1a01-41ad-9fa3-3269d15755da'::uuid, '9ae57c49-af20-440b-a94f-9a33344d012c'::uuid,
                    '5c2f2e3b-4b85-4ee0-b855-0aad4290088f'::uuid, 'ffbb2c76-2185-48a3-8e1e-200b47d47652'::uuid);
                DELETE FROM role_permissions WHERE permission_id IN (
                    '7ceb1441-6396-4245-8ab8-020712168942'::uuid, '5a85c6fb-065e-4d96-8e65-11d7ed536e66'::uuid,
                    '2ad0e01f-1a01-41ad-9fa3-3269d15755da'::uuid, '9ae57c49-af20-440b-a94f-9a33344d012c'::uuid,
                    '5c2f2e3b-4b85-4ee0-b855-0aad4290088f'::uuid, 'ffbb2c76-2185-48a3-8e1e-200b47d47652'::uuid);
                """);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("2ad0e01f-1a01-41ad-9fa3-3269d15755da"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5a85c6fb-065e-4d96-8e65-11d7ed536e66"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5c2f2e3b-4b85-4ee0-b855-0aad4290088f"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("7ceb1441-6396-4245-8ab8-020712168942"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("9ae57c49-af20-440b-a94f-9a33344d012c"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ffbb2c76-2185-48a3-8e1e-200b47d47652"));
        }
    }
}
