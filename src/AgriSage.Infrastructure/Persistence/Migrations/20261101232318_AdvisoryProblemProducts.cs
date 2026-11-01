using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvisoryProblemProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "problem_type",
                table: "diseases",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DISEASE");

            migrationBuilder.AddColumn<string>(
                name: "search_keywords",
                table: "diseases",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "advisory_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    diagnosis_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    query_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    area_sqm = table.Column<decimal>(type: "numeric(12,1)", precision: 12, scale: 1, nullable: true),
                    growth_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advisory_sessions", x => x.id);
                    table.CheckConstraint("ck_advisory_sessions_area_sqm", "area_sqm IS NULL OR area_sqm > 0");
                    table.CheckConstraint("ck_advisory_sessions_growth_stage", "growth_stage IN ('SEEDLING', 'TILLERING', 'BOOTING', 'HEADING', 'RIPENING')");
                    table.CheckConstraint("ck_advisory_sessions_source", "source IN ('TEXT', 'DIAGNOSIS_CASE')");
                    table.ForeignKey(
                        name: "fk_advisory_sessions_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_advisory_sessions_diagnosis_case_id",
                        column: x => x.diagnosis_case_id,
                        principalTable: "diagnosis_cases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_advisory_sessions_disease_id",
                        column: x => x.disease_id,
                        principalTable: "diseases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advisory_sessions_farmer_profile_id",
                        column: x => x.farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advisory_sessions_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disease_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    disease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_group = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registration_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    dose_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    dose_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dose_per_ha_base = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    applications = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    pre_harvest_days = table.Column<int>(type: "integer", nullable: true),
                    growth_stages = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disease_products", x => x.id);
                    table.CheckConstraint("ck_disease_products_applications", "applications BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_disease_products_approval", "status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_disease_products_dose_per_ha_base", "dose_per_ha_base IS NULL OR dose_per_ha_base > 0");
                    table.CheckConstraint("ck_disease_products_pre_harvest_days", "pre_harvest_days IS NULL OR pre_harvest_days >= 0");
                    table.CheckConstraint("ck_disease_products_reason", "recommendation_group <> 'NUTRITION' OR reason IS NOT NULL");
                    table.CheckConstraint("ck_disease_products_recommendation_group", "recommendation_group IN ('PROTECTION', 'NUTRITION')");
                    table.CheckConstraint("ck_disease_products_registration_source", "recommendation_group <> 'PROTECTION' OR registration_source IS NOT NULL");
                    table.CheckConstraint("ck_disease_products_status", "status IN ('DRAFT', 'APPROVED', 'INACTIVE')");
                    table.ForeignKey(
                        name: "fk_disease_products_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_disease_products_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_disease_products_disease_id",
                        column: x => x.disease_id,
                        principalTable: "diseases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disease_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "advisory_session_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    advisory_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    disease_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank_order = table.Column<int>(type: "integer", nullable: false),
                    suggested_base_quantity = table.Column<long>(type: "bigint", nullable: true),
                    estimated_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advisory_session_items", x => x.id);
                    table.CheckConstraint("ck_advisory_session_items_suggested_base_quantity", "suggested_base_quantity IS NULL OR suggested_base_quantity > 0");
                    table.ForeignKey(
                        name: "fk_advisory_session_items_advisory_session_id",
                        column: x => x.advisory_session_id,
                        principalTable: "advisory_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advisory_session_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_advisory_session_items_disease_product_id",
                        column: x => x.disease_product_id,
                        principalTable: "disease_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advisory_session_items_store_product_id",
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
                    { new Guid("0b105d26-9577-4d4a-b543-da6462da3239"), "DISEASE_PRODUCTS.UPDATE", true, false, "DISEASE_PRODUCTS", "Sửa" },
                    { new Guid("0ffa96d2-14aa-4e8a-afdc-fac7e4a0b7dc"), "DISEASE_PRODUCTS.APPROVE", true, false, "DISEASE_PRODUCTS", "Duyệt" },
                    { new Guid("3ef7a81d-d4b6-4765-95a7-25fe71f50077"), "DISEASES.CREATE", true, false, "DISEASES", "Tạo" },
                    { new Guid("6685ff65-dfae-4548-b4ae-45cf3b5516e1"), "MY_ADVISORY.READ", true, false, "MY_ADVISORY", "Xem" },
                    { new Guid("68125cc1-2493-4215-9eae-0db63c3093df"), "ADVISORY_SESSIONS.READ", true, false, "ADVISORY_SESSIONS", "Xem" },
                    { new Guid("72f2c147-3d41-48cb-b468-8fe1bf840734"), "DISEASE_PRODUCTS.DEACTIVATE", true, false, "DISEASE_PRODUCTS", "Ngừng hoạt động" },
                    { new Guid("744b80a0-66dc-43ee-82c3-94cc495b379d"), "DISEASE_PRODUCTS.READ", true, false, "DISEASE_PRODUCTS", "Xem" },
                    { new Guid("ee3bc3cf-0d99-45bd-912c-ea87589bc125"), "DISEASE_PRODUCTS.CREATE", true, false, "DISEASE_PRODUCTS", "Tạo" }
                });

            // Role defaults of the new permissions (the same shape as AiDiagnosisPermissions): ADMIN gets all of them, every
            // other role the ones its catalog entry lists. Re-running is harmless.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (id, role_id, permission_id, is_granted, created_at, updated_at)
                SELECT gen_random_uuid(), r.id, defaults.permission_id, TRUE, now(), now()
                FROM (VALUES
                    ('ADMIN', '6685ff65-dfae-4548-b4ae-45cf3b5516e1'::uuid),
                    ('FARMER', '6685ff65-dfae-4548-b4ae-45cf3b5516e1'::uuid),
                    ('ADMIN', '3ef7a81d-d4b6-4765-95a7-25fe71f50077'::uuid),
                    ('STORE_OWNER', '3ef7a81d-d4b6-4765-95a7-25fe71f50077'::uuid),
                    ('ADMIN', '744b80a0-66dc-43ee-82c3-94cc495b379d'::uuid),
                    ('SALES_STAFF', '744b80a0-66dc-43ee-82c3-94cc495b379d'::uuid),
                    ('STORE_OWNER', '744b80a0-66dc-43ee-82c3-94cc495b379d'::uuid),
                    ('ADMIN', 'ee3bc3cf-0d99-45bd-912c-ea87589bc125'::uuid),
                    ('STORE_OWNER', 'ee3bc3cf-0d99-45bd-912c-ea87589bc125'::uuid),
                    ('ADMIN', '0b105d26-9577-4d4a-b543-da6462da3239'::uuid),
                    ('STORE_OWNER', '0b105d26-9577-4d4a-b543-da6462da3239'::uuid),
                    ('ADMIN', '0ffa96d2-14aa-4e8a-afdc-fac7e4a0b7dc'::uuid),
                    ('STORE_OWNER', '0ffa96d2-14aa-4e8a-afdc-fac7e4a0b7dc'::uuid),
                    ('ADMIN', '72f2c147-3d41-48cb-b468-8fe1bf840734'::uuid),
                    ('STORE_OWNER', '72f2c147-3d41-48cb-b468-8fe1bf840734'::uuid),
                    ('ADMIN', '68125cc1-2493-4215-9eae-0db63c3093df'::uuid),
                    ('SALES_STAFF', '68125cc1-2493-4215-9eae-0db63c3093df'::uuid),
                    ('STORE_OWNER', '68125cc1-2493-4215-9eae-0db63c3093df'::uuid)
                ) AS defaults(role_code, permission_id)
                JOIN roles r ON r.code = defaults.role_code AND r.deleted_at IS NULL
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_diseases_problem_type",
                table: "diseases",
                sql: "problem_type IN ('DISEASE', 'PEST', 'WEED', 'NUTRITION', 'GROWTH')");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_session_items_advisory_session_id",
                table: "advisory_session_items",
                column: "advisory_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_session_items_disease_product_id",
                table: "advisory_session_items",
                column: "disease_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_session_items_store_product_id",
                table: "advisory_session_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_sessions_diagnosis_case_id",
                table: "advisory_sessions",
                column: "diagnosis_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_sessions_disease_id",
                table: "advisory_sessions",
                column: "disease_id");

            migrationBuilder.CreateIndex(
                name: "ix_advisory_sessions_farmer_profile_id_disease_id_created_at",
                table: "advisory_sessions",
                columns: new[] { "farmer_profile_id", "disease_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_advisory_sessions_store_id_created_at",
                table: "advisory_sessions",
                columns: new[] { "store_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_disease_products_disease_id_status",
                table: "disease_products",
                columns: new[] { "disease_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_disease_products_product_id",
                table: "disease_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_disease_products_disease_id_product_id",
                table: "disease_products",
                columns: new[] { "disease_id", "product_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // role_permissions references permissions with RESTRICT: remove the defaults of these permissions first.
            migrationBuilder.Sql("""
                DELETE FROM role_permissions
                WHERE permission_id IN (
                    '6685ff65-dfae-4548-b4ae-45cf3b5516e1'::uuid,
                    '3ef7a81d-d4b6-4765-95a7-25fe71f50077'::uuid,
                    '744b80a0-66dc-43ee-82c3-94cc495b379d'::uuid,
                    'ee3bc3cf-0d99-45bd-912c-ea87589bc125'::uuid,
                    '0b105d26-9577-4d4a-b543-da6462da3239'::uuid,
                    '0ffa96d2-14aa-4e8a-afdc-fac7e4a0b7dc'::uuid,
                    '72f2c147-3d41-48cb-b468-8fe1bf840734'::uuid,
                    '68125cc1-2493-4215-9eae-0db63c3093df'::uuid
                );
                """);

            migrationBuilder.DropTable(
                name: "advisory_session_items");

            migrationBuilder.DropTable(
                name: "advisory_sessions");

            migrationBuilder.DropTable(
                name: "disease_products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_diseases_problem_type",
                table: "diseases");

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0b105d26-9577-4d4a-b543-da6462da3239"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("0ffa96d2-14aa-4e8a-afdc-fac7e4a0b7dc"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("3ef7a81d-d4b6-4765-95a7-25fe71f50077"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("6685ff65-dfae-4548-b4ae-45cf3b5516e1"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("68125cc1-2493-4215-9eae-0db63c3093df"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("72f2c147-3d41-48cb-b468-8fe1bf840734"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("744b80a0-66dc-43ee-82c3-94cc495b379d"));

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "id",
                keyValue: new Guid("ee3bc3cf-0d99-45bd-912c-ea87589bc125"));

            migrationBuilder.DropColumn(
                name: "problem_type",
                table: "diseases");

            migrationBuilder.DropColumn(
                name: "search_keywords",
                table: "diseases");
        }
    }
}
