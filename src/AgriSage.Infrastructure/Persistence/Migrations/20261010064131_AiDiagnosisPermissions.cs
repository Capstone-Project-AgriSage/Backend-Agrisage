using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiDiagnosisPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "id", "code", "is_active", "is_delegable", "module", "name" },
                values: new object[,]
                {
                    { new Guid("0733527b-ed6b-71f1-445e-a77051eeaaba"), "MY_DIAGNOSIS.CREATE", true, false, "MY_DIAGNOSIS", "Tạo" },
                    { new Guid("08802d94-e5a5-91b8-4eb6-9fcb9fb58e6f"), "AI_POLICIES.READ", true, false, "AI_POLICIES", "Xem" },
                    { new Guid("0d80faf6-3fae-595f-6f7e-cc0d4f68d9a3"), "AI_MODELS.READ", true, false, "AI_MODELS", "Xem" },
                    { new Guid("1b37130f-ce9f-8139-5ef7-34ead6277a97"), "DIAGNOSIS.UNRECOMMEND", true, true, "DIAGNOSIS", "Unrecommend" },
                    { new Guid("247b2a92-5ed8-97bf-36c3-5fc619e8727e"), "DIAGNOSIS.REVIEW", true, true, "DIAGNOSIS", "Duyệt" },
                    { new Guid("2d18d223-48b9-8bcd-faeb-3ccd374099d6"), "DISEASE_TREATMENTS.CREATE", true, false, "DISEASE_TREATMENTS", "Tạo" },
                    { new Guid("3096c728-7cb3-d63b-7a77-9362c3f303a6"), "DIAGNOSIS.START_REVIEW", true, true, "DIAGNOSIS", "Bắt đầu duyệt" },
                    { new Guid("46fc583c-8887-8782-9c73-977f3dc42ea1"), "AI_MODELS.RETIRE", true, false, "AI_MODELS", "Retire" },
                    { new Guid("5507eeeb-089f-6faa-e0ce-57bfb277ec51"), "MY_DIAGNOSIS.READ", true, false, "MY_DIAGNOSIS", "Xem" },
                    { new Guid("612dc910-13ca-6689-c2ea-93153a05ef99"), "DISEASES.UPDATE", true, false, "DISEASES", "Sửa" },
                    { new Guid("71cdf210-3026-f5c4-b686-ec6f9cc2eee7"), "AI_MODELS.CREATE", true, false, "AI_MODELS", "Tạo" },
                    { new Guid("7c802e5d-521a-9096-18ba-9c92e0624d2b"), "STAFF.SET_AI_REVIEW", true, false, "STAFF", "Set Ai Review" },
                    { new Guid("98a54aff-24d4-fb26-0d83-219cdee0727c"), "DISEASE_TREATMENTS.ACTIVATE", true, false, "DISEASE_TREATMENTS", "Kích hoạt" },
                    { new Guid("99fa135c-a48c-5fb7-2992-ab64f7659aec"), "DIAGNOSIS.RECOMMEND", true, true, "DIAGNOSIS", "Recommend" },
                    { new Guid("9ca7a59c-d319-6bf6-ed5e-3be6fe837f89"), "DISEASE_TREATMENTS.DEACTIVATE", true, false, "DISEASE_TREATMENTS", "Ngừng hoạt động" },
                    { new Guid("b63eaecf-5143-1f5b-9d16-0deb1943be52"), "DISEASES.READ", true, false, "DISEASES", "Xem" },
                    { new Guid("b99bb424-a582-c7d7-a7f3-2e149a7c49f5"), "AI_POLICIES.CREATE", true, false, "AI_POLICIES", "Tạo" },
                    { new Guid("c680f235-11e1-a355-2d06-20e94d1ef50c"), "AI_MODELS.ACTIVATE", true, false, "AI_MODELS", "Kích hoạt" },
                    { new Guid("cc0c3532-efca-43d4-4b6d-9d50d8c11a3a"), "DISEASE_TREATMENTS.UPDATE", true, false, "DISEASE_TREATMENTS", "Sửa" },
                    { new Guid("cfc7b786-02fc-8a75-12e2-95b0aca0bd07"), "DIAGNOSIS.RERUN_AI", true, true, "DIAGNOSIS", "Rerun Ai" },
                    { new Guid("d70d0e12-955a-4226-7027-50be98955a47"), "AI_POLICIES.ACTIVATE", true, false, "AI_POLICIES", "Kích hoạt" },
                    { new Guid("d93c9a37-1eb5-d8c0-99d3-230288d129f5"), "AI_POLICIES.DEACTIVATE", true, false, "AI_POLICIES", "Ngừng hoạt động" },
                    { new Guid("ee12b9df-1bc3-327f-64fb-a06d3f6bdabc"), "DIAGNOSIS.READ", true, true, "DIAGNOSIS", "Xem" },
                    { new Guid("f9a4db05-ac3e-9c42-36bf-e7f85d554ead"), "MY_DIAGNOSIS.CANCEL", true, false, "MY_DIAGNOSIS", "Hủy" }
                });

            // Role defaults of the new permissions (the same shape as the DynamicPermissions migration): ADMIN gets all of
            // them, every other role the ones its catalog entry lists. Re-running is harmless.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', '0733527b-ed6b-71f1-445e-a77051eeaaba'::uuid),
                    ('FARMER', '0733527b-ed6b-71f1-445e-a77051eeaaba'::uuid),
                    ('ADMIN', '5507eeeb-089f-6faa-e0ce-57bfb277ec51'::uuid),
                    ('FARMER', '5507eeeb-089f-6faa-e0ce-57bfb277ec51'::uuid),
                    ('ADMIN', 'f9a4db05-ac3e-9c42-36bf-e7f85d554ead'::uuid),
                    ('FARMER', 'f9a4db05-ac3e-9c42-36bf-e7f85d554ead'::uuid),
                    ('ADMIN', 'ee12b9df-1bc3-327f-64fb-a06d3f6bdabc'::uuid),
                    ('SALES_STAFF', 'ee12b9df-1bc3-327f-64fb-a06d3f6bdabc'::uuid),
                    ('STORE_OWNER', 'ee12b9df-1bc3-327f-64fb-a06d3f6bdabc'::uuid),
                    ('ADMIN', '3096c728-7cb3-d63b-7a77-9362c3f303a6'::uuid),
                    ('SALES_STAFF', '3096c728-7cb3-d63b-7a77-9362c3f303a6'::uuid),
                    ('STORE_OWNER', '3096c728-7cb3-d63b-7a77-9362c3f303a6'::uuid),
                    ('ADMIN', '247b2a92-5ed8-97bf-36c3-5fc619e8727e'::uuid),
                    ('SALES_STAFF', '247b2a92-5ed8-97bf-36c3-5fc619e8727e'::uuid),
                    ('STORE_OWNER', '247b2a92-5ed8-97bf-36c3-5fc619e8727e'::uuid),
                    ('ADMIN', 'cfc7b786-02fc-8a75-12e2-95b0aca0bd07'::uuid),
                    ('SALES_STAFF', 'cfc7b786-02fc-8a75-12e2-95b0aca0bd07'::uuid),
                    ('STORE_OWNER', 'cfc7b786-02fc-8a75-12e2-95b0aca0bd07'::uuid),
                    ('ADMIN', '99fa135c-a48c-5fb7-2992-ab64f7659aec'::uuid),
                    ('SALES_STAFF', '99fa135c-a48c-5fb7-2992-ab64f7659aec'::uuid),
                    ('STORE_OWNER', '99fa135c-a48c-5fb7-2992-ab64f7659aec'::uuid),
                    ('ADMIN', '1b37130f-ce9f-8139-5ef7-34ead6277a97'::uuid),
                    ('SALES_STAFF', '1b37130f-ce9f-8139-5ef7-34ead6277a97'::uuid),
                    ('STORE_OWNER', '1b37130f-ce9f-8139-5ef7-34ead6277a97'::uuid),
                    ('ADMIN', '0d80faf6-3fae-595f-6f7e-cc0d4f68d9a3'::uuid),
                    ('STORE_OWNER', '0d80faf6-3fae-595f-6f7e-cc0d4f68d9a3'::uuid),
                    ('ADMIN', '71cdf210-3026-f5c4-b686-ec6f9cc2eee7'::uuid),
                    ('STORE_OWNER', '71cdf210-3026-f5c4-b686-ec6f9cc2eee7'::uuid),
                    ('ADMIN', 'c680f235-11e1-a355-2d06-20e94d1ef50c'::uuid),
                    ('STORE_OWNER', 'c680f235-11e1-a355-2d06-20e94d1ef50c'::uuid),
                    ('ADMIN', '46fc583c-8887-8782-9c73-977f3dc42ea1'::uuid),
                    ('STORE_OWNER', '46fc583c-8887-8782-9c73-977f3dc42ea1'::uuid),
                    ('ADMIN', '08802d94-e5a5-91b8-4eb6-9fcb9fb58e6f'::uuid),
                    ('STORE_OWNER', '08802d94-e5a5-91b8-4eb6-9fcb9fb58e6f'::uuid),
                    ('ADMIN', 'b99bb424-a582-c7d7-a7f3-2e149a7c49f5'::uuid),
                    ('STORE_OWNER', 'b99bb424-a582-c7d7-a7f3-2e149a7c49f5'::uuid),
                    ('ADMIN', 'd70d0e12-955a-4226-7027-50be98955a47'::uuid),
                    ('STORE_OWNER', 'd70d0e12-955a-4226-7027-50be98955a47'::uuid),
                    ('ADMIN', 'd93c9a37-1eb5-d8c0-99d3-230288d129f5'::uuid),
                    ('STORE_OWNER', 'd93c9a37-1eb5-d8c0-99d3-230288d129f5'::uuid),
                    ('ADMIN', 'b63eaecf-5143-1f5b-9d16-0deb1943be52'::uuid),
                    ('SALES_STAFF', 'b63eaecf-5143-1f5b-9d16-0deb1943be52'::uuid),
                    ('STORE_OWNER', 'b63eaecf-5143-1f5b-9d16-0deb1943be52'::uuid),
                    ('ADMIN', '612dc910-13ca-6689-c2ea-93153a05ef99'::uuid),
                    ('STORE_OWNER', '612dc910-13ca-6689-c2ea-93153a05ef99'::uuid),
                    ('ADMIN', '2d18d223-48b9-8bcd-faeb-3ccd374099d6'::uuid),
                    ('STORE_OWNER', '2d18d223-48b9-8bcd-faeb-3ccd374099d6'::uuid),
                    ('ADMIN', 'cc0c3532-efca-43d4-4b6d-9d50d8c11a3a'::uuid),
                    ('STORE_OWNER', 'cc0c3532-efca-43d4-4b6d-9d50d8c11a3a'::uuid),
                    ('ADMIN', '98a54aff-24d4-fb26-0d83-219cdee0727c'::uuid),
                    ('STORE_OWNER', '98a54aff-24d4-fb26-0d83-219cdee0727c'::uuid),
                    ('ADMIN', '9ca7a59c-d319-6bf6-ed5e-3be6fe837f89'::uuid),
                    ('STORE_OWNER', '9ca7a59c-d319-6bf6-ed5e-3be6fe837f89'::uuid),
                    ('ADMIN', '7c802e5d-521a-9096-18ba-9c92e0624d2b'::uuid),
                    ('STORE_OWNER', '7c802e5d-521a-9096-18ba-9c92e0624d2b'::uuid)
                ) AS defaults(role_code, permission_id)
                JOIN roles r ON r.code = defaults.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // role_permissions references permissions with RESTRICT: remove the defaults of these permissions first.
            migrationBuilder.Sql("""
                DELETE FROM role_permissions
                WHERE permission_id IN (
                    '0733527b-ed6b-71f1-445e-a77051eeaaba'::uuid,
                    '08802d94-e5a5-91b8-4eb6-9fcb9fb58e6f'::uuid,
                    '0d80faf6-3fae-595f-6f7e-cc0d4f68d9a3'::uuid,
                    '1b37130f-ce9f-8139-5ef7-34ead6277a97'::uuid,
                    '247b2a92-5ed8-97bf-36c3-5fc619e8727e'::uuid,
                    '2d18d223-48b9-8bcd-faeb-3ccd374099d6'::uuid,
                    '3096c728-7cb3-d63b-7a77-9362c3f303a6'::uuid,
                    '46fc583c-8887-8782-9c73-977f3dc42ea1'::uuid,
                    '5507eeeb-089f-6faa-e0ce-57bfb277ec51'::uuid,
                    '612dc910-13ca-6689-c2ea-93153a05ef99'::uuid,
                    '71cdf210-3026-f5c4-b686-ec6f9cc2eee7'::uuid,
                    '7c802e5d-521a-9096-18ba-9c92e0624d2b'::uuid,
                    '98a54aff-24d4-fb26-0d83-219cdee0727c'::uuid,
                    '99fa135c-a48c-5fb7-2992-ab64f7659aec'::uuid,
                    '9ca7a59c-d319-6bf6-ed5e-3be6fe837f89'::uuid,
                    'b63eaecf-5143-1f5b-9d16-0deb1943be52'::uuid,
                    'b99bb424-a582-c7d7-a7f3-2e149a7c49f5'::uuid,
                    'c680f235-11e1-a355-2d06-20e94d1ef50c'::uuid,
                    'cc0c3532-efca-43d4-4b6d-9d50d8c11a3a'::uuid,
                    'cfc7b786-02fc-8a75-12e2-95b0aca0bd07'::uuid,
                    'd70d0e12-955a-4226-7027-50be98955a47'::uuid,
                    'd93c9a37-1eb5-d8c0-99d3-230288d129f5'::uuid,
                    'ee12b9df-1bc3-327f-64fb-a06d3f6bdabc'::uuid,
                    'f9a4db05-ac3e-9c42-36bf-e7f85d554ead'::uuid
                );
                """);

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0733527b-ed6b-71f1-445e-a77051eeaaba"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("08802d94-e5a5-91b8-4eb6-9fcb9fb58e6f"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0d80faf6-3fae-595f-6f7e-cc0d4f68d9a3"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("1b37130f-ce9f-8139-5ef7-34ead6277a97"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("247b2a92-5ed8-97bf-36c3-5fc619e8727e"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("2d18d223-48b9-8bcd-faeb-3ccd374099d6"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("3096c728-7cb3-d63b-7a77-9362c3f303a6"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("46fc583c-8887-8782-9c73-977f3dc42ea1"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("5507eeeb-089f-6faa-e0ce-57bfb277ec51"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("612dc910-13ca-6689-c2ea-93153a05ef99"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("71cdf210-3026-f5c4-b686-ec6f9cc2eee7"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("7c802e5d-521a-9096-18ba-9c92e0624d2b"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("98a54aff-24d4-fb26-0d83-219cdee0727c"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("99fa135c-a48c-5fb7-2992-ab64f7659aec"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("9ca7a59c-d319-6bf6-ed5e-3be6fe837f89"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("b63eaecf-5143-1f5b-9d16-0deb1943be52"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("b99bb424-a582-c7d7-a7f3-2e149a7c49f5"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("c680f235-11e1-a355-2d06-20e94d1ef50c"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cc0c3532-efca-43d4-4b6d-9d50d8c11a3a"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("cfc7b786-02fc-8a75-12e2-95b0aca0bd07"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("d70d0e12-955a-4226-7027-50be98955a47"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("d93c9a37-1eb5-d8c0-99d3-230288d129f5"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ee12b9df-1bc3-327f-64fb-a06d3f6bdabc"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("f9a4db05-ac3e-9c42-36bf-e7f85d554ead"));
        }
    }
}
