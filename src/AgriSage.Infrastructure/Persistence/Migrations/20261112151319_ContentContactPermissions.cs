using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentContactPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0001"), "ARTICLES.READ", true, false, "ARTICLES", "Xem bài viết" },
                    { new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0002"), "ARTICLES.MANAGE", true, false, "ARTICLES", "Quản lý bài viết" },
                    { new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0003"), "CONTACT_REQUESTS.READ", true, true, "CONTACT_REQUESTS", "Xem liên hệ" },
                    { new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0004"), "CONTACT_REQUESTS.MANAGE", true, true, "CONTACT_REQUESTS", "Xử lý liên hệ" },
                    { new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0005"), "MY_CONTACT_REQUESTS.READ", true, false, "MY_CONTACT_REQUESTS", "Xem liên hệ của tôi" }
                });
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', 'f9a22c22-7ddd-4b7d-a801-163a754d0001'::uuid),
                    ('STORE_OWNER', 'f9a22c22-7ddd-4b7d-a801-163a754d0001'::uuid),
                    ('ADMIN', 'f9a22c22-7ddd-4b7d-a801-163a754d0002'::uuid),
                    ('STORE_OWNER', 'f9a22c22-7ddd-4b7d-a801-163a754d0002'::uuid),
                    ('ADMIN', 'f9a22c22-7ddd-4b7d-a801-163a754d0003'::uuid),
                    ('STORE_OWNER', 'f9a22c22-7ddd-4b7d-a801-163a754d0003'::uuid),
                    ('SALES_STAFF', 'f9a22c22-7ddd-4b7d-a801-163a754d0003'::uuid),
                    ('ADMIN', 'f9a22c22-7ddd-4b7d-a801-163a754d0004'::uuid),
                    ('STORE_OWNER', 'f9a22c22-7ddd-4b7d-a801-163a754d0004'::uuid),
                    ('SALES_STAFF', 'f9a22c22-7ddd-4b7d-a801-163a754d0004'::uuid),
                    ('ADMIN', 'f9a22c22-7ddd-4b7d-a801-163a754d0005'::uuid),
                    ('FARMER', 'f9a22c22-7ddd-4b7d-a801-163a754d0005'::uuid)
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
                    'f9a22c22-7ddd-4b7d-a801-163a754d0001'::uuid, 'f9a22c22-7ddd-4b7d-a801-163a754d0002'::uuid,
                    'f9a22c22-7ddd-4b7d-a801-163a754d0003'::uuid, 'f9a22c22-7ddd-4b7d-a801-163a754d0004'::uuid,
                    'f9a22c22-7ddd-4b7d-a801-163a754d0005'::uuid);
                DELETE FROM role_permissions WHERE permission_id IN (
                    'f9a22c22-7ddd-4b7d-a801-163a754d0001'::uuid, 'f9a22c22-7ddd-4b7d-a801-163a754d0002'::uuid,
                    'f9a22c22-7ddd-4b7d-a801-163a754d0003'::uuid, 'f9a22c22-7ddd-4b7d-a801-163a754d0004'::uuid,
                    'f9a22c22-7ddd-4b7d-a801-163a754d0005'::uuid);
                """);
            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0001"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0002"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0003"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0004"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a22c22-7ddd-4b7d-a801-163a754d0005"));
        }
    }
}
