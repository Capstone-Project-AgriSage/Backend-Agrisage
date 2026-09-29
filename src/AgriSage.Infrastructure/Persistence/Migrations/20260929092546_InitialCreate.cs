using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgriSage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_ingredients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    diagnosis_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_ai_inference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewer_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ai_disease_id_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    final_disease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by_review_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_reviews", x => x.id);
                    table.CheckConstraint("ck_agent_reviews_decision", "decision IN ('CONFIRMED', 'CORRECTED', 'INCONCLUSIVE')");
                    table.CheckConstraint("ck_agent_reviews_decision_final_disease", "(decision IN ('CONFIRMED', 'CORRECTED') AND final_disease_id IS NOT NULL) OR (decision = 'INCONCLUSIVE' AND final_disease_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_agent_reviews_superseded_by_review_id",
                        column: x => x.superseded_by_review_id,
                        principalTable: "agent_reviews",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "ai_inferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    diagnosis_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnosis_image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_policy_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predicted_disease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    predicted_class_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    top_predictions = table.Column<string>(type: "jsonb", nullable: true),
                    raw_output = table.Column<string>(type: "jsonb", nullable: true),
                    passed_policy = table.Column<bool>(type: "boolean", nullable: false),
                    inference_duration_ms = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    inferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_inferences", x => x.id);
                    table.CheckConstraint("ck_ai_inferences_confidence", "confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_ai_inferences_inference_duration_ms", "inference_duration_ms IS NULL OR inference_duration_ms >= 0");
                    table.CheckConstraint("ck_ai_inferences_status", "status IN ('SUCCESS', 'FAILED')");
                });

            migrationBuilder.CreateTable(
                name: "ai_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    architecture = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    framework = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model_storage_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    input_width = table.Column<int>(type: "integer", nullable: true),
                    input_height = table.Column<int>(type: "integer", nullable: true),
                    class_labels = table.Column<string>(type: "jsonb", nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deployed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_models", x => x.id);
                    table.CheckConstraint("ck_ai_models_status", "status IN ('DRAFT', 'ACTIVE', 'RETIRED')");
                });

            migrationBuilder.CreateTable(
                name: "ai_policy_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ai_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    minimum_confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    minimum_margin = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    top_k = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    requires_human_review = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    parameters = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_policy_configs", x => x.id);
                    table.CheckConstraint("ck_ai_policy_configs_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_ai_policy_configs_minimum_confidence", "minimum_confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_ai_policy_configs_minimum_margin", "minimum_margin IS NULL OR minimum_margin BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_ai_policy_configs_status", "status IN ('DRAFT', 'ACTIVE', 'INACTIVE')");
                    table.CheckConstraint("ck_ai_policy_configs_top_k", "top_k > 0");
                    table.ForeignKey(
                        name: "fk_ai_policy_configs_ai_model_id",
                        column: x => x.ai_model_id,
                        principalTable: "ai_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articles", x => x.id);
                    table.CheckConstraint("ck_articles_published_at", "status <> 'PUBLISHED' OR published_at IS NOT NULL");
                    table.CheckConstraint("ck_articles_status", "status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')");
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_packaging_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_items", x => x.id);
                    table.CheckConstraint("ck_cart_items_quantity", "quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "carts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    converted_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                    table.CheckConstraint("ck_carts_status", "status IN ('ACTIVE', 'CONVERTED', 'ABANDONED')");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_parent_id",
                        column: x => x.parent_id,
                        principalTable: "categories",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "contact_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    request_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_requests", x => x.id);
                    table.CheckConstraint("ck_contact_requests_status", "status IN ('OPEN', 'IN_PROGRESS', 'RESOLVED', 'CLOSED')");
                });

            migrationBuilder.CreateTable(
                name: "credit_limit_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farmer_credit_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_credit_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_credit_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    new_credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_limit_histories", x => x.id);
                    table.CheckConstraint("ck_credit_limit_histories_new_credit_limit", "new_credit_limit >= 0");
                    table.CheckConstraint("ck_credit_limit_histories_old_credit_limit", "old_credit_limit >= 0");
                });

            migrationBuilder.CreateTable(
                name: "credit_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_credit_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_reserved = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_consumed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    amount_released = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reserved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    release_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_reservations", x => x.id);
                    table.CheckConstraint("ck_credit_reservations_amount_consumed", "amount_consumed >= 0");
                    table.CheckConstraint("ck_credit_reservations_amount_released", "amount_released >= 0");
                    table.CheckConstraint("ck_credit_reservations_amount_reserved", "amount_reserved >= 0");
                    table.CheckConstraint("ck_credit_reservations_consumed_released_within_reserved", "amount_consumed + amount_released <= amount_reserved");
                    table.CheckConstraint("ck_credit_reservations_status", "status IN ('ACTIVE', 'PARTIALLY_CONSUMED', 'CONSUMED', 'RELEASED', 'CANCELLED')");
                });

            migrationBuilder.CreateTable(
                name: "credit_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    default_credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    default_payment_term_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_tiers", x => x.id);
                    table.CheckConstraint("ck_credit_tiers_default_credit_limit", "default_credit_limit >= 0");
                    table.CheckConstraint("ck_credit_tiers_default_payment_term_days", "default_payment_term_days >= 0");
                });

            migrationBuilder.CreateTable(
                name: "customer_group_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_group_assignments", x => x.id);
                    table.CheckConstraint("ck_customer_group_assignments_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                });

            migrationBuilder.CreateTable(
                name: "customer_group_price_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_group_price_lists", x => x.id);
                    table.CheckConstraint("ck_customer_group_price_lists_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                });

            migrationBuilder.CreateTable(
                name: "customer_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "debt_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_transaction_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_accounts", x => x.id);
                    table.CheckConstraint("ck_debt_accounts_current_balance", "current_balance >= 0");
                    table.CheckConstraint("ck_debt_accounts_status", "status IN ('ACTIVE', 'BLOCKED', 'CLOSED')");
                });

            migrationBuilder.CreateTable(
                name: "debt_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    debt_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fulfillment_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    prepayment_applied_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    original_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    outstanding_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_entries", x => x.id);
                    table.CheckConstraint("ck_debt_entries_fulfillment_value", "fulfillment_value >= 0");
                    table.CheckConstraint("ck_debt_entries_original_amount", "original_amount >= 0");
                    table.CheckConstraint("ck_debt_entries_original_amount_formula", "original_amount = fulfillment_value - prepayment_applied_amount");
                    table.CheckConstraint("ck_debt_entries_outstanding_amount", "outstanding_amount >= 0");
                    table.CheckConstraint("ck_debt_entries_outstanding_within_original", "outstanding_amount <= original_amount");
                    table.CheckConstraint("ck_debt_entries_prepayment_applied_amount", "prepayment_applied_amount >= 0");
                    table.CheckConstraint("ck_debt_entries_prepayment_within_fulfillment", "prepayment_applied_amount <= fulfillment_value");
                    table.CheckConstraint("ck_debt_entries_source_references", "(source_type = 'DELIVERY' AND order_id IS NOT NULL AND delivery_id IS NOT NULL AND source_stock_movement_id IS NOT NULL) OR (source_type = 'PICKUP' AND order_id IS NOT NULL AND source_stock_movement_id IS NOT NULL) OR source_type = 'MANUAL_ADJUSTMENT'");
                    table.CheckConstraint("ck_debt_entries_source_type", "source_type IN ('DELIVERY', 'PICKUP', 'MANUAL_ADJUSTMENT')");
                    table.CheckConstraint("ck_debt_entries_status", "status IN ('OPEN', 'PARTIALLY_PAID', 'PAID', 'DISPUTED', 'ADJUSTED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_debt_entries_debt_account_id",
                        column: x => x.debt_account_id,
                        principalTable: "debt_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debt_entry_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    debt_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    previous_outstanding_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    adjustment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    resulting_outstanding_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    old_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    new_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_entry_actions", x => x.id);
                    table.CheckConstraint("ck_debt_entry_actions_action_type", "action_type IN ('DISPUTE', 'KEEP', 'ADJUST', 'CANCEL', 'CHANGE_DUE_DATE')");
                    table.ForeignKey(
                        name: "fk_debt_entry_actions_debt_entry_id",
                        column: x => x.debt_entry_id,
                        principalTable: "debt_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debt_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    debt_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debt_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debt_entry_action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_delta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reversal_of_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_transactions", x => x.id);
                    table.CheckConstraint("ck_debt_transactions_amount_delta", "amount_delta <> 0");
                    table.CheckConstraint("ck_debt_transactions_balance_after", "balance_after >= 0");
                    table.CheckConstraint("ck_debt_transactions_payment_allocation_source", "transaction_type <> 'PAYMENT' OR payment_allocation_id IS NOT NULL");
                    table.CheckConstraint("ck_debt_transactions_status", "status IN ('POSTED', 'REVERSED')");
                    table.CheckConstraint("ck_debt_transactions_transaction_type", "transaction_type IN ('CREDIT_SALE', 'PAYMENT', 'ADJUSTMENT_IN', 'ADJUSTMENT_OUT', 'RETURN', 'REVERSAL')");
                    table.ForeignKey(
                        name: "fk_debt_transactions_debt_account_id",
                        column: x => x.debt_account_id,
                        principalTable: "debt_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debt_transactions_debt_entry_action_id",
                        column: x => x.debt_entry_action_id,
                        principalTable: "debt_entry_actions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_debt_transactions_debt_entry_id",
                        column: x => x.debt_entry_id,
                        principalTable: "debt_entries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_debt_transactions_reversal_of_transaction_id",
                        column: x => x.reversal_of_transaction_id,
                        principalTable: "debt_transactions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    assigned_to_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    recipient_phone_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_line_snapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ward_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    province_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    latitude_snapshot = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    longitude_snapshot = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                    table.CheckConstraint("ck_deliveries_status", "status IN ('DRAFT', 'ASSIGNED', 'OUT_FOR_DELIVERY', 'PARTIALLY_DELIVERED', 'RETRY_PENDING', 'DELIVERED', 'CANCELLED')");
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempt_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_item_lot_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempted_base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    delivered_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    failed_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempt_items", x => x.id);
                    table.CheckConstraint("ck_delivery_attempt_items_attempted_base_quantity", "attempted_base_quantity > 0");
                    table.CheckConstraint("ck_delivery_attempt_items_delivered_base_quantity", "delivered_base_quantity >= 0");
                    table.CheckConstraint("ck_delivery_attempt_items_delivered_failed_within_attempted", "delivered_base_quantity + failed_base_quantity <= attempted_base_quantity");
                    table.CheckConstraint("ck_delivery_attempt_items_failed_base_quantity", "failed_base_quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    attempted_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    receiver_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    proof_image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sale_stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                    table.CheckConstraint("ck_delivery_attempts_status", "status IN ('IN_PROGRESS', 'SUCCESS', 'PARTIAL_SUCCESS', 'FAILED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_delivery_attempts_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_item_lot_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incident_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    affected_base_quantity = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    related_stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_incidents", x => x.id);
                    table.CheckConstraint("ck_delivery_incidents_incident_type", "incident_type IN ('CUSTOMER_ABSENT', 'UNREACHABLE', 'CUSTOMER_REFUSED', 'DAMAGED', 'WEATHER', 'VEHICLE_ISSUE', 'ADDRESS_ISSUE', 'OTHER')");
                    table.CheckConstraint("ck_delivery_incidents_resolution_type", "resolution_type IN ('RETRY_DELIVERY', 'REPLACE_GOODS', 'RETURN_TO_STORE', 'WRITE_OFF', 'CANCEL_REMAINDER', 'NO_ACTION', 'OTHER')");
                    table.CheckConstraint("ck_delivery_incidents_status", "status IN ('OPEN', 'RESOLVED')");
                    table.ForeignKey(
                        name: "fk_delivery_incidents_delivery_attempt_id",
                        column: x => x.delivery_attempt_id,
                        principalTable: "delivery_attempts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_delivery_incidents_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_item_lot_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_reservation_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    delivered_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    released_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_item_lot_allocations", x => x.id);
                    table.CheckConstraint("ck_delivery_item_lot_allocations_allocated_base_quantity", "allocated_base_quantity > 0");
                    table.CheckConstraint("ck_delivery_item_lot_allocations_delivered_base_quantity", "delivered_base_quantity >= 0");
                    table.CheckConstraint("ck_delivery_item_lot_allocations_released_base_quantity", "released_base_quantity >= 0");
                    table.CheckConstraint("ck_delivery_item_lot_allocations_status", "status IN ('ALLOCATED', 'PARTIALLY_DELIVERED', 'DELIVERED', 'RELEASED', 'CANCELLED')");
                    table.CheckConstraint("ck_delivery_item_lot_allocations_within_allocated", "delivered_base_quantity + released_base_quantity <= allocated_base_quantity");
                });

            migrationBuilder.CreateTable(
                name: "delivery_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_quantity = table.Column<long>(type: "bigint", nullable: false),
                    conversion_to_base_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    planned_base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    delivered_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    cancelled_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_items", x => x.id);
                    table.CheckConstraint("ck_delivery_items_cancelled_base_quantity", "cancelled_base_quantity >= 0");
                    table.CheckConstraint("ck_delivery_items_conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
                    table.CheckConstraint("ck_delivery_items_delivered_base_quantity", "delivered_base_quantity >= 0");
                    table.CheckConstraint("ck_delivery_items_delivered_cancelled_within_planned", "delivered_base_quantity + cancelled_base_quantity <= planned_base_quantity");
                    table.CheckConstraint("ck_delivery_items_planned_base_quantity", "planned_base_quantity = planned_quantity * conversion_to_base_snapshot");
                    table.CheckConstraint("ck_delivery_items_planned_quantity", "planned_quantity > 0");
                    table.CheckConstraint("ck_delivery_items_status", "status IN ('PENDING', 'PARTIALLY_DELIVERED', 'DELIVERED', 'CANCELLED', 'PARTIALLY_CANCELLED')");
                    table.ForeignKey(
                        name: "fk_delivery_items_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "diagnosis_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    crop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RICE"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    final_disease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    farmer_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    review_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnosis_cases", x => x.id);
                    table.CheckConstraint("ck_diagnosis_cases_inconclusive_final_disease", "status <> 'INCONCLUSIVE' OR final_disease_id IS NULL");
                    table.CheckConstraint("ck_diagnosis_cases_status", "status IN ('SUBMITTED', 'PROCESSING', 'AI_COMPLETED', 'UNDER_REVIEW', 'VERIFIED', 'INCONCLUSIVE', 'FAILED', 'CANCELLED')");
                    table.CheckConstraint("ck_diagnosis_cases_verified_final_disease", "status <> 'VERIFIED' OR final_disease_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "diagnosis_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    diagnosis_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    image_url = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnosis_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagnosis_images_diagnosis_case_id",
                        column: x => x.diagnosis_case_id,
                        principalTable: "diagnosis_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disease_treatments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    disease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_ingredient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    treatment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    instructions = table.Column<string>(type: "text", nullable: false),
                    precautions = table.Column<string>(type: "text", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disease_treatments", x => x.id);
                    table.CheckConstraint("ck_disease_treatments_treatment_type", "treatment_type IN ('CULTURAL', 'CHEMICAL', 'PREVENTIVE', 'OTHER')");
                    table.ForeignKey(
                        name: "fk_disease_treatments_active_ingredient_id",
                        column: x => x.active_ingredient_id,
                        principalTable: "active_ingredients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "diseases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scientific_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    crop_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "RICE"),
                    description = table.Column<string>(type: "text", nullable: true),
                    symptoms = table.Column<string>(type: "text", nullable: true),
                    causes = table.Column<string>(type: "text", nullable: true),
                    prevention = table.Column<string>(type: "text", nullable: true),
                    is_healthy_class = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diseases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "farmer_credit_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_tier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farmer_credit_profiles", x => x.id);
                    table.CheckConstraint("ck_farmer_credit_profiles_credit_limit", "credit_limit >= 0");
                    table.CheckConstraint("ck_farmer_credit_profiles_status", "status IN ('ACTIVE', 'SUSPENDED', 'BLOCKED')");
                    table.ForeignKey(
                        name: "fk_farmer_credit_profiles_credit_tier_id",
                        column: x => x.credit_tier_id,
                        principalTable: "credit_tiers",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "farmer_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farmer_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_packaging_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_quantity = table.Column<long>(type: "bigint", nullable: false),
                    conversion_to_base_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    purchase_unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    base_unit_cost = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    supplier_lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_items", x => x.id);
                    table.CheckConstraint("ck_goods_receipt_items_base_unit_cost", "base_unit_cost >= 0");
                    table.CheckConstraint("ck_goods_receipt_items_conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
                    table.CheckConstraint("ck_goods_receipt_items_line_total_amount", "line_total_amount >= 0");
                    table.CheckConstraint("ck_goods_receipt_items_purchase_unit_cost", "purchase_unit_cost >= 0");
                    table.CheckConstraint("ck_goods_receipt_items_received_quantity", "received_quantity > 0");
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    supplier_invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    source_file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.CheckConstraint("ck_goods_receipts_source_type", "source_type IN ('MANUAL', 'EXCEL_TEMPLATE')");
                    table.CheckConstraint("ck_goods_receipts_status", "status IN ('DRAFT', 'CONFIRMED', 'CANCELLED')");
                    table.CheckConstraint("ck_goods_receipts_subtotal_amount", "subtotal_amount >= 0");
                    table.CheckConstraint("ck_goods_receipts_total_amount", "total_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_lot_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    quantity_reserved = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    total_cost_value = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false, defaultValue: 0m),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_lot_balances", x => x.id);
                    table.CheckConstraint("ck_inventory_lot_balances_quantity_on_hand", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_inventory_lot_balances_quantity_reserved", "quantity_reserved >= 0");
                    table.CheckConstraint("ck_inventory_lot_balances_reserved_within_on_hand", "quantity_reserved <= quantity_on_hand");
                    table.CheckConstraint("ck_inventory_lot_balances_total_cost_value", "total_cost_value >= 0");
                    table.CheckConstraint("ck_inventory_lot_balances_zero_quantity_zero_cost", "quantity_on_hand > 0 OR total_cost_value = 0");
                });

            migrationBuilder.CreateTable(
                name: "inventory_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_lots", x => x.id);
                    table.CheckConstraint("ck_inventory_lots_status", "status IN ('ACTIVE', 'QUARANTINED', 'EXPIRED', 'BLOCKED', 'DEPLETED')");
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    inventory_reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_quantity_reserved = table.Column<long>(type: "bigint", nullable: false),
                    base_quantity_consumed = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    base_quantity_released = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservation_items", x => x.id);
                    table.CheckConstraint("ck_inventory_reservation_items_base_quantity_consumed", "base_quantity_consumed >= 0");
                    table.CheckConstraint("ck_inventory_reservation_items_base_quantity_released", "base_quantity_released >= 0");
                    table.CheckConstraint("ck_inventory_reservation_items_base_quantity_reserved", "base_quantity_reserved > 0");
                    table.CheckConstraint("ck_inventory_reservation_items_within_reserved", "base_quantity_consumed + base_quantity_released <= base_quantity_reserved");
                    table.ForeignKey(
                        name: "fk_inventory_reservation_items_inventory_lot_id",
                        column: x => x.inventory_lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reserved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reserved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    release_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_reservations", x => x.id);
                    table.CheckConstraint("ck_inventory_reservations_status", "status IN ('ACTIVE', 'PARTIALLY_CONSUMED', 'CONSUMED', 'RELEASED', 'CANCELLED')");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "UNREAD"),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_status", "status IN ('UNREAD', 'READ', 'ARCHIVED')");
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_packaging_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_sku_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    packaging_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    conversion_to_base_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    suggested_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_overridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    override_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overridden_by = table.Column<Guid>(type: "uuid", nullable: true),
                    fulfilled_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    cancelled_base_quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_base_quantity", "base_quantity = quantity * conversion_to_base_snapshot");
                    table.CheckConstraint("ck_order_items_cancelled_base_quantity", "cancelled_base_quantity >= 0");
                    table.CheckConstraint("ck_order_items_conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
                    table.CheckConstraint("ck_order_items_fulfilled_base_quantity", "fulfilled_base_quantity >= 0");
                    table.CheckConstraint("ck_order_items_fulfilled_cancelled_within_base", "fulfilled_base_quantity + cancelled_base_quantity <= base_quantity");
                    table.CheckConstraint("ck_order_items_line_total_amount", "line_total_amount >= 0");
                    table.CheckConstraint("ck_order_items_price_override", "NOT price_overridden OR (override_reason IS NOT NULL AND overridden_by IS NOT NULL)");
                    table.CheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.CheckConstraint("ck_order_items_status", "status IN ('PENDING', 'PARTIALLY_FULFILLED', 'FULFILLED', 'CANCELLED', 'PARTIALLY_CANCELLED')");
                    table.CheckConstraint("ck_order_items_suggested_unit_price", "suggested_unit_price >= 0");
                    table.CheckConstraint("ck_order_items_unit_price", "unit_price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_group_id_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    price_list_id_snapshot = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    customer_phone_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    settlement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    credit_term_days_snapshot = table.Column<int>(type: "integer", nullable: true),
                    fulfillment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    recipient_phone_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    delivery_address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivery_ward = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    delivery_district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    delivery_province = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    delivery_latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    delivery_longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    pickup_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pickup_completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_credit_term_days_snapshot", "credit_term_days_snapshot IS NULL OR credit_term_days_snapshot >= 0");
                    table.CheckConstraint("ck_orders_customer_settlement", "(customer_type = 'REGISTERED' AND farmer_profile_id IS NOT NULL) OR (customer_type = 'WALK_IN' AND farmer_profile_id IS NULL AND settlement_type = 'FULL_PAYMENT')");
                    table.CheckConstraint("ck_orders_customer_type", "customer_type IN ('REGISTERED', 'WALK_IN')");
                    table.CheckConstraint("ck_orders_fulfillment_type", "fulfillment_type IN ('PICKUP', 'DELIVERY')");
                    table.CheckConstraint("ck_orders_settlement_type", "settlement_type IN ('FULL_PAYMENT', 'CREDIT')");
                    table.CheckConstraint("ck_orders_source", "source IN ('FARMER_WEB', 'FARMER_MOBILE', 'COUNTER')");
                    table.CheckConstraint("ck_orders_status", "status IN ('PENDING_CONFIRMATION', 'CONFIRMED', 'PREPARING', 'READY_FOR_FULFILLMENT', 'PARTIALLY_FULFILLED', 'COMPLETED', 'CANCELLED', 'PARTIALLY_CANCELLED')");
                    table.CheckConstraint("ck_orders_subtotal_amount", "subtotal_amount >= 0");
                    table.CheckConstraint("ck_orders_total_amount", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_orders_customer_group_id_snapshot",
                        column: x => x.customer_group_id_snapshot,
                        principalTable: "customer_groups",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_orders_farmer_profile_id",
                        column: x => x.farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocation_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debt_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    prepayment_consumed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    allocated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_allocated_amount", "allocated_amount > 0");
                    table.CheckConstraint("ck_payment_allocations_allocation_type", "allocation_type IN ('ORDER', 'DEBT')");
                    table.CheckConstraint("ck_payment_allocations_consumed_within_allocated", "prepayment_consumed_amount <= allocated_amount");
                    table.CheckConstraint("ck_payment_allocations_prepayment_consumed_amount", "prepayment_consumed_amount >= 0");
                    table.CheckConstraint("ck_payment_allocations_single_target", "(allocation_type = 'ORDER' AND order_id IS NOT NULL AND debt_entry_id IS NULL) OR (allocation_type = 'DEBT' AND debt_entry_id IS NOT NULL AND order_id IS NULL AND prepayment_consumed_amount = 0)");
                    table.CheckConstraint("ck_payment_allocations_status", "status IN ('ACTIVE', 'REVERSED')");
                    table.ForeignKey(
                        name: "fk_payment_allocations_debt_entry_id",
                        column: x => x.debt_entry_id,
                        principalTable: "debt_entries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_allocations_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payer_farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_context = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "VND"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    provider_order_code = table.Column<long>(type: "bigint", nullable: true),
                    provider_payment_link_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    provider_transaction_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    provider_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    confirmation_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_confirmation_source", "confirmation_source IN ('STAFF', 'PAYOS_WEBHOOK')");
                    table.CheckConstraint("ck_payments_payment_context", "payment_context IN ('ORDER_PAYMENT', 'DEBT_REPAYMENT')");
                    table.CheckConstraint("ck_payments_payment_method", "payment_method IN ('CASH', 'PAYOS')");
                    table.CheckConstraint("ck_payments_status", "status IN ('PENDING', 'PAID', 'FAILED', 'CANCELLED', 'PARTIALLY_REFUNDED', 'REFUNDED')");
                    table.ForeignKey(
                        name: "fk_payments_payer_farmer_profile_id",
                        column: x => x.payer_farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_packaging_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selling_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_list_items", x => x.id);
                    table.CheckConstraint("ck_price_list_items_selling_price", "selling_price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_walk_in_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_lists", x => x.id);
                    table.CheckConstraint("ck_price_lists_effective_period", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_price_lists_status", "status IN ('DRAFT', 'ACTIVE', 'INACTIVE')");
                });

            migrationBuilder.CreateTable(
                name: "product_active_ingredients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concentration = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_active_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_active_ingredients_active_ingredient_id",
                        column: x => x.active_ingredient_id,
                        principalTable: "active_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_packagings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    packaging_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    conversion_to_base = table.Column<long>(type: "bigint", nullable: false),
                    is_base_unit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_purchase_unit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_sale_unit = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_packagings", x => x.id);
                    table.CheckConstraint("ck_product_packagings_base_unit_conversion", "NOT is_base_unit OR conversion_to_base = 1");
                    table.CheckConstraint("ck_product_packagings_conversion_to_base", "conversion_to_base > 0");
                });

            migrationBuilder.CreateTable(
                name: "product_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_reviews", x => x.id);
                    table.CheckConstraint("ck_product_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_product_reviews_farmer_profile_id",
                        column: x => x.farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_reviews_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    usage_instructions = table.Column<string>(type: "text", nullable: true),
                    requires_lot_tracking = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    requires_expiry_date = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    image_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_status", "status IN ('ACTIVE', 'INACTIVE', 'DISCONTINUED')");
                    table.ForeignKey(
                        name: "fk_products_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_products_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    diagnosis_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommendation_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    disease_treatment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    store_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rank_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_items", x => x.id);
                    table.CheckConstraint("ck_recommendation_items_recommendation_type", "recommendation_type IN ('TREATMENT', 'PRODUCT')");
                    table.CheckConstraint("ck_recommendation_items_single_target", "(recommendation_type = 'TREATMENT' AND disease_treatment_id IS NOT NULL AND store_product_id IS NULL) OR (recommendation_type = 'PRODUCT' AND store_product_id IS NOT NULL AND disease_treatment_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_recommendation_items_agent_review_id",
                        column: x => x.agent_review_id,
                        principalTable: "agent_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recommendation_items_diagnosis_case_id",
                        column: x => x.diagnosis_case_id,
                        principalTable: "diagnosis_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recommendation_items_disease_treatment_id",
                        column: x => x.disease_treatment_id,
                        principalTable: "disease_treatments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refund_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "VND"),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    proof_file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount", "amount > 0");
                    table.CheckConstraint("ck_refunds_refund_method", "refund_method IN ('CASH', 'BANK_TRANSFER', 'OTHER_EXTERNAL')");
                    table.CheckConstraint("ck_refunds_status", "status IN ('PENDING', 'COMPLETED', 'FAILED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_refunds_original_payment_id",
                        column: x => x.original_payment_id,
                        principalTable: "payments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                    table.CheckConstraint("ck_roles_code", "code IN ('FARMER', 'STORE_OWNER', 'SALES_STAFF', 'DELIVERY_STAFF', 'ADMIN')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    phone_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_contact", "NULLIF(BTRIM(email), '') IS NOT NULL OR NULLIF(BTRIM(phone_number), '') IS NOT NULL");
                    table.CheckConstraint("ck_users_status", "status IN ('ACTIVE', 'INACTIVE', 'SUSPENDED', 'LOCKED')");
                    table.ForeignKey(
                        name: "fk_users_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_users_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ward = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    province = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                    table.CheckConstraint("ck_stores_status", "status IN ('ACTIVE', 'INACTIVE')");
                    table.ForeignKey(
                        name: "fk_stores_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_units_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ward = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    province = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    address_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_addresses", x => x.id);
                    table.CheckConstraint("ck_user_addresses_address_type", "address_type IN ('HOME', 'FARM', 'OTHER')");
                    table.ForeignKey(
                        name: "fk_user_addresses_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_addresses_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_returns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    inspected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inspected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_return_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    total_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    total_debt_adjustment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_returns", x => x.id);
                    table.CheckConstraint("ck_sales_returns_status", "status IN ('REQUESTED', 'APPROVED', 'REJECTED', 'RECEIVED', 'INSPECTED', 'PARTIALLY_RESOLVED', 'COMPLETED', 'CANCELLED')");
                    table.CheckConstraint("ck_sales_returns_total_debt_adjustment", "total_debt_adjustment >= 0");
                    table.CheckConstraint("ck_sales_returns_total_refund_amount", "total_refund_amount >= 0");
                    table.CheckConstraint("ck_sales_returns_total_return_amount", "total_return_amount >= 0");
                    table.ForeignKey(
                        name: "fk_sales_returns_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_cancelled_by",
                        column: x => x.cancelled_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_farmer_profile_id",
                        column: x => x.farmer_profile_id,
                        principalTable: "farmer_profiles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_inspected_by",
                        column: x => x.inspected_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_received_by",
                        column: x => x.received_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_returns_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_returns_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocktakes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stocktake_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stocktakes", x => x.id);
                    table.CheckConstraint("ck_stocktakes_status", "status IN ('DRAFT', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_stocktakes_completed_by",
                        column: x => x.completed_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stocktakes_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktakes_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stocktakes_started_by",
                        column: x => x.started_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stocktakes_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    joined_at = table.Column<DateOnly>(type: "date", nullable: true),
                    left_at = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    can_review_ai = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_members", x => x.id);
                    table.CheckConstraint("ck_store_members_status", "status IN ('ACTIVE', 'INACTIVE', 'LEFT')");
                    table.ForeignKey(
                        name: "fk_store_members_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_store_members_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_members_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    min_stock_level_base = table.Column<long>(type: "bigint", nullable: true),
                    is_sellable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_products_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_store_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_products_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ward = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    district = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    province = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "fk_suppliers_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_suppliers_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    movement_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stocktake_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_of_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_movement_type", "movement_type IN ('STOCK_IN', 'SALE', 'RETURN_IN', 'ADJUSTMENT_IN', 'ADJUSTMENT_OUT', 'REVERSAL')");
                    table.CheckConstraint("ck_stock_movements_status", "status IN ('DRAFT', 'POSTED', 'REVERSED', 'CANCELLED')");
                    table.ForeignKey(
                        name: "fk_stock_movements_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalTable: "goods_receipts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_posted_by",
                        column: x => x.posted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_reversal_of_movement_id",
                        column: x => x.reversal_of_movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_sales_return_id",
                        column: x => x.sales_return_id,
                        principalTable: "sales_returns",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_stocktake_id",
                        column: x => x.stocktake_id,
                        principalTable: "stocktakes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movements_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stocktake_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    system_quantity_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    counted_quantity = table.Column<long>(type: "bigint", nullable: true),
                    difference_quantity = table.Column<long>(type: "bigint", nullable: true),
                    unit_cost_snapshot = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    difference_cost_value = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    counted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    counted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stocktake_items", x => x.id);
                    table.CheckConstraint("ck_stocktake_items_counted_quantity", "counted_quantity IS NULL OR counted_quantity >= 0");
                    table.CheckConstraint("ck_stocktake_items_system_quantity_snapshot", "system_quantity_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_stocktake_items_counted_by",
                        column: x => x.counted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stocktake_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stocktake_items_inventory_lot_id",
                        column: x => x.inventory_lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stocktake_items_stocktake_id",
                        column: x => x.stocktake_id,
                        principalTable: "stocktakes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    stock_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_delta_base = table.Column<long>(type: "bigint", nullable: false),
                    unit_cost_snapshot = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    total_cost_snapshot = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    quantity_on_hand_after = table.Column<long>(type: "bigint", nullable: true),
                    total_cost_value_after = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movement_items", x => x.id);
                    table.CheckConstraint("ck_stock_movement_items_quantity_delta_base", "quantity_delta_base <> 0");
                    table.CheckConstraint("ck_stock_movement_items_total_cost_snapshot", "total_cost_snapshot >= 0");
                    table.CheckConstraint("ck_stock_movement_items_unit_cost_snapshot", "unit_cost_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_stock_movement_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_stock_movement_items_inventory_lot_id",
                        column: x => x.inventory_lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movement_items_stock_movement_id",
                        column: x => x.stock_movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_return_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    sales_return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_item_lot_allocation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_stock_movement_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    returned_base_quantity = table.Column<long>(type: "bigint", nullable: false),
                    selling_unit_price_snapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    conversion_to_base_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    return_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_cogs_unit_cost = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    return_inventory_cost_value = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    condition_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    inventory_disposition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    inspection_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    return_stock_movement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debt_adjustment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_return_items", x => x.id);
                    table.CheckConstraint("ck_sales_return_items_condition_status", "condition_status IN ('PENDING_INSPECTION', 'RESELLABLE', 'DAMAGED', 'EXPIRED', 'UNUSABLE')");
                    table.CheckConstraint("ck_sales_return_items_conversion_to_base_snapshot", "conversion_to_base_snapshot > 0");
                    table.CheckConstraint("ck_sales_return_items_inventory_disposition", "inventory_disposition IN ('NONE', 'RESTOCK', 'WRITE_OFF')");
                    table.CheckConstraint("ck_sales_return_items_original_cogs_unit_cost", "original_cogs_unit_cost IS NULL OR original_cogs_unit_cost >= 0");
                    table.CheckConstraint("ck_sales_return_items_return_inventory_cost_value", "return_inventory_cost_value IS NULL OR return_inventory_cost_value >= 0");
                    table.CheckConstraint("ck_sales_return_items_return_value", "return_value >= 0");
                    table.CheckConstraint("ck_sales_return_items_returned_base_quantity", "returned_base_quantity > 0");
                    table.CheckConstraint("ck_sales_return_items_selling_unit_price_snapshot", "selling_unit_price_snapshot >= 0");
                    table.CheckConstraint("ck_sales_return_items_single_fulfillment_source", "(delivery_item_lot_allocation_id IS NULL) <> (original_stock_movement_item_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_sales_return_items_debt_adjustment_transaction_id",
                        column: x => x.debt_adjustment_transaction_id,
                        principalTable: "debt_transactions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_deleted_by",
                        column: x => x.deleted_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_delivery_item_id",
                        column: x => x.delivery_item_id,
                        principalTable: "delivery_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_delivery_item_lot_allocation_id",
                        column: x => x.delivery_item_lot_allocation_id,
                        principalTable: "delivery_item_lot_allocations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_inventory_lot_id",
                        column: x => x.inventory_lot_id,
                        principalTable: "inventory_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_items_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_return_items_original_stock_movement_item_id",
                        column: x => x.original_stock_movement_item_id,
                        principalTable: "stock_movement_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_return_stock_movement_id",
                        column: x => x.return_stock_movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sales_return_items_sales_return_id",
                        column: x => x.sales_return_id,
                        principalTable: "sales_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_ai_disease_id_snapshot",
                table: "agent_reviews",
                column: "ai_disease_id_snapshot");

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_diagnosis_case_id_reviewed_at",
                table: "agent_reviews",
                columns: new[] { "diagnosis_case_id", "reviewed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_final_disease_id",
                table: "agent_reviews",
                column: "final_disease_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_primary_ai_inference_id",
                table: "agent_reviews",
                column: "primary_ai_inference_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_reviewer_member_id_reviewed_at",
                table: "agent_reviews",
                columns: new[] { "reviewer_member_id", "reviewed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_superseded_by_review_id",
                table: "agent_reviews",
                column: "superseded_by_review_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_reviews_current",
                table: "agent_reviews",
                column: "diagnosis_case_id",
                unique: true,
                filter: "is_current = TRUE AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ai_inferences_ai_model_id",
                table: "ai_inferences",
                column: "ai_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_inferences_ai_policy_config_id",
                table: "ai_inferences",
                column: "ai_policy_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_inferences_diagnosis_case_id_inferred_at",
                table: "ai_inferences",
                columns: new[] { "diagnosis_case_id", "inferred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_inferences_diagnosis_image_id",
                table: "ai_inferences",
                column: "diagnosis_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_inferences_predicted_disease_id",
                table: "ai_inferences",
                column: "predicted_disease_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_models_name_version",
                table: "ai_models",
                columns: new[] { "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_policy_configs_ai_model_id_status_effective_from",
                table: "ai_policy_configs",
                columns: new[] { "ai_model_id", "status", "effective_from" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_ai_policy_configs_ai_model_id_version",
                table: "ai_policy_configs",
                columns: new[] { "ai_model_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_articles_status_published_at",
                table: "articles",
                columns: new[] { "status", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_articles_slug",
                table: "articles",
                column: "slug",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "actor_user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id", "occurred_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at",
                table: "audit_logs",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_store_id",
                table: "audit_logs",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_brands_name",
                table: "brands",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_id",
                table: "cart_items",
                column: "cart_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_product_packaging_id",
                table: "cart_items",
                column: "product_packaging_id");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_store_product_id",
                table: "cart_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_cart_items_cart_id_store_product_id_product_packaging_id",
                table: "cart_items",
                columns: new[] { "cart_id", "store_product_id", "product_packaging_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_carts_converted_order_id",
                table: "carts",
                column: "converted_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_carts_farmer_profile_id",
                table: "carts",
                column: "farmer_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_carts_store_id",
                table: "carts",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_carts_store_id_farmer_profile_id",
                table: "carts",
                columns: new[] { "store_id", "farmer_profile_id" },
                unique: true,
                filter: "status = 'ACTIVE' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_assigned_to_status",
                table: "contact_requests",
                columns: new[] { "assigned_to", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_status_created_at",
                table: "contact_requests",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_user_id",
                table: "contact_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_contact_requests_request_number",
                table: "contact_requests",
                column: "request_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_credit_limit_histories_farmer_credit_profile_id",
                table: "credit_limit_histories",
                column: "farmer_credit_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_limit_histories_new_credit_tier_id",
                table: "credit_limit_histories",
                column: "new_credit_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_limit_histories_old_credit_tier_id",
                table: "credit_limit_histories",
                column: "old_credit_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_reservations_farmer_credit_profile_id_status",
                table: "credit_reservations",
                columns: new[] { "farmer_credit_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_reservations_order_id",
                table: "credit_reservations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_reservations_store_id",
                table: "credit_reservations",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_credit_reservations_open_order",
                table: "credit_reservations",
                column: "order_id",
                unique: true,
                filter: "status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_credit_tiers_store_id_code",
                table: "credit_tiers",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_group_assignments_customer_group_id_effective_from",
                table: "customer_group_assignments",
                columns: new[] { "customer_group_id", "effective_from" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_customer_group_assignments_farmer_profile_id_effective_from",
                table: "customer_group_assignments",
                columns: new[] { "farmer_profile_id", "effective_from" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_customer_group_assignments_current",
                table: "customer_group_assignments",
                column: "farmer_profile_id",
                unique: true,
                filter: "effective_to IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customer_group_price_lists_customer_group_id_effective_from",
                table: "customer_group_price_lists",
                columns: new[] { "customer_group_id", "effective_from" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_customer_group_price_lists_price_list_id",
                table: "customer_group_price_lists",
                column: "price_list_id");

            migrationBuilder.CreateIndex(
                name: "ux_customer_groups_default",
                table: "customer_groups",
                column: "store_id",
                unique: true,
                filter: "is_default AND is_active AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_customer_groups_store_id_code",
                table: "customer_groups",
                columns: new[] { "store_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_accounts_farmer_profile_id",
                table: "debt_accounts",
                column: "farmer_profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_debt_accounts_store_id_farmer_profile_id",
                table: "debt_accounts",
                columns: new[] { "store_id", "farmer_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_debt_account_id_status_due_date",
                table: "debt_entries",
                columns: new[] { "debt_account_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_delivery_attempt_id",
                table: "debt_entries",
                column: "delivery_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_delivery_id",
                table: "debt_entries",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_order_id",
                table: "debt_entries",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_source_stock_movement_id",
                table: "debt_entries",
                column: "source_stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_entries_status_due_date",
                table: "debt_entries",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ux_debt_entries_entry_number",
                table: "debt_entries",
                column: "entry_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_entry_actions_debt_entry_id_created_at",
                table: "debt_entry_actions",
                columns: new[] { "debt_entry_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_debt_account_id_occurred_at",
                table: "debt_transactions",
                columns: new[] { "debt_account_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_debt_entry_action_id",
                table: "debt_transactions",
                column: "debt_entry_action_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_debt_entry_id_occurred_at",
                table: "debt_transactions",
                columns: new[] { "debt_entry_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_payment_allocation_id",
                table: "debt_transactions",
                column: "payment_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_reversal_of_transaction_id",
                table: "debt_transactions",
                column: "reversal_of_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_transactions_sales_return_id",
                table: "debt_transactions",
                column: "sales_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_assigned_to_member_id_status_scheduled_at",
                table: "deliveries",
                columns: new[] { "assigned_to_member_id", "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_order_id",
                table: "deliveries",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_status_scheduled_at",
                table: "deliveries",
                columns: new[] { "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_store_id_delivery_number",
                table: "deliveries",
                columns: new[] { "store_id", "delivery_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempt_items_delivery_attempt_id",
                table: "delivery_attempt_items",
                column: "delivery_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempt_items_delivery_item_lot_allocation_id",
                table: "delivery_attempt_items",
                column: "delivery_item_lot_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_attempt_allocation",
                table: "delivery_attempt_items",
                columns: new[] { "delivery_attempt_id", "delivery_item_lot_allocation_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_attempted_by_member_id",
                table: "delivery_attempts",
                column: "attempted_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_sale_stock_movement_id",
                table: "delivery_attempts",
                column: "sale_stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ux_delivery_attempts_delivery_id_attempt_number",
                table: "delivery_attempts",
                columns: new[] { "delivery_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_incidents_delivery_attempt_id",
                table: "delivery_incidents",
                column: "delivery_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_incidents_delivery_id_status",
                table: "delivery_incidents",
                columns: new[] { "delivery_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_incidents_delivery_item_lot_allocation_id",
                table: "delivery_incidents",
                column: "delivery_item_lot_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_incidents_related_stock_movement_id",
                table: "delivery_incidents",
                column: "related_stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_item_lot_allocations_delivery_item_id",
                table: "delivery_item_lot_allocations",
                column: "delivery_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_item_lot_allocations_inventory_lot_id",
                table: "delivery_item_lot_allocations",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_item_lot_allocations_inventory_reservation_item_id",
                table: "delivery_item_lot_allocations",
                column: "inventory_reservation_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_items_delivery_id",
                table: "delivery_items",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_items_order_item_id",
                table: "delivery_items",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagnosis_cases_farmer_profile_id_submitted_at",
                table: "diagnosis_cases",
                columns: new[] { "farmer_profile_id", "submitted_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_diagnosis_cases_final_disease_id",
                table: "diagnosis_cases",
                column: "final_disease_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagnosis_cases_status_submitted_at",
                table: "diagnosis_cases",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_diagnosis_cases_case_number",
                table: "diagnosis_cases",
                column: "case_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnosis_images_diagnosis_case_id_uploaded_at",
                table: "diagnosis_images",
                columns: new[] { "diagnosis_case_id", "uploaded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_diagnosis_images_primary",
                table: "diagnosis_images",
                column: "diagnosis_case_id",
                unique: true,
                filter: "is_primary = TRUE AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_disease_treatments_active_ingredient_id",
                table: "disease_treatments",
                column: "active_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_disease_treatments_disease_id",
                table: "disease_treatments",
                column: "disease_id");

            migrationBuilder.CreateIndex(
                name: "ux_diseases_code",
                table: "diseases",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_farmer_credit_profiles_credit_tier_id",
                table: "farmer_credit_profiles",
                column: "credit_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_farmer_credit_profiles_farmer_profile_id",
                table: "farmer_credit_profiles",
                column: "farmer_profile_id");

            migrationBuilder.CreateIndex(
                name: "ux_farmer_credit_profiles_store_id_farmer_profile_id",
                table: "farmer_credit_profiles",
                columns: new[] { "store_id", "farmer_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_farmer_profiles_user_id",
                table: "farmer_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_items_goods_receipt_id",
                table: "goods_receipt_items",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_items_inventory_lot_id",
                table: "goods_receipt_items",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_items_product_packaging_id",
                table: "goods_receipt_items",
                column: "product_packaging_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_items_store_product_id",
                table: "goods_receipt_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_status",
                table: "goods_receipts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_store_id_received_at",
                table: "goods_receipts",
                columns: new[] { "store_id", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_supplier_id_received_at",
                table: "goods_receipts",
                columns: new[] { "supplier_id", "received_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_goods_receipts_store_id_receipt_number",
                table: "goods_receipts",
                columns: new[] { "store_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inventory_lot_balances_inventory_lot_id",
                table: "inventory_lot_balances",
                column: "inventory_lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_status_expiry_date",
                table: "inventory_lots",
                columns: new[] { "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_store_product_id_expiry_date",
                table: "inventory_lots",
                columns: new[] { "store_product_id", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "ux_inventory_lots_no_lot_bucket",
                table: "inventory_lots",
                column: "store_product_id",
                unique: true,
                filter: "lot_number IS NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_items_inventory_lot_id",
                table: "inventory_reservation_items",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_items_inventory_reservation_id",
                table: "inventory_reservation_items",
                column: "inventory_reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservation_items_order_item_id",
                table: "inventory_reservation_items",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_reservation_item_lot",
                table: "inventory_reservation_items",
                columns: new[] { "inventory_reservation_id", "order_item_id", "inventory_lot_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservations_order_id",
                table: "inventory_reservations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_reservations_store_id",
                table: "inventory_reservations",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_reservations_open_order",
                table: "inventory_reservations",
                column: "order_id",
                unique: true,
                filter: "status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_status_created_at",
                table: "notifications",
                columns: new[] { "user_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_product_packaging_id",
                table: "order_items",
                column: "product_packaging_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_status",
                table: "order_items",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_store_product_id",
                table: "order_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_group_id_snapshot",
                table: "orders",
                column: "customer_group_id_snapshot");

            migrationBuilder.CreateIndex(
                name: "ix_orders_farmer_profile_id_created_at",
                table: "orders",
                columns: new[] { "farmer_profile_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_fulfillment_type_status",
                table: "orders",
                columns: new[] { "fulfillment_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_price_list_id_snapshot",
                table: "orders",
                column: "price_list_id_snapshot");

            migrationBuilder.CreateIndex(
                name: "ix_orders_settlement_type_status",
                table: "orders",
                columns: new[] { "settlement_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_source_address_id",
                table: "orders",
                column: "source_address_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_created_at",
                table: "orders",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_created_at",
                table: "orders",
                columns: new[] { "store_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_orders_store_id_order_number",
                table: "orders",
                columns: new[] { "store_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_debt_entry_id",
                table: "payment_allocations",
                column: "debt_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_order_id",
                table: "payment_allocations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_status",
                table: "payment_allocations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payer_farmer_profile_id_initiated_at",
                table: "payments",
                columns: new[] { "payer_farmer_profile_id", "initiated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_initiated_at",
                table: "payments",
                columns: new[] { "status", "initiated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_order_code",
                table: "payments",
                column: "provider_order_code",
                unique: true,
                filter: "provider_order_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_store_id_payment_number",
                table: "payments",
                columns: new[] { "store_id", "payment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_product_packaging_id",
                table: "price_list_items",
                column: "product_packaging_id");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_items_store_product_id",
                table: "price_list_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_price_list_items_price_list_id_store_product_id_product_pack",
                table: "price_list_items",
                columns: new[] { "price_list_id", "store_product_id", "product_packaging_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_lists_store_id_status_effective_from",
                table: "price_lists",
                columns: new[] { "store_id", "status", "effective_from" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_price_lists_walk_in_default",
                table: "price_lists",
                column: "store_id",
                unique: true,
                filter: "is_walk_in_default AND status = 'ACTIVE' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_active_ingredients_active_ingredient_id",
                table: "product_active_ingredients",
                column: "active_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_active_ingredients_product_id_active_ingredient_id",
                table: "product_active_ingredients",
                columns: new[] { "product_id", "active_ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_packagings_product_id_status",
                table: "product_packagings",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_product_packagings_unit_id",
                table: "product_packagings",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_packagings_barcode",
                table: "product_packagings",
                column: "barcode",
                unique: true,
                filter: "barcode IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_product_packagings_base_unit",
                table: "product_packagings",
                column: "product_id",
                unique: true,
                filter: "is_base_unit AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_product_packagings_product_id_unit_id",
                table: "product_packagings",
                columns: new[] { "product_id", "unit_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_order_item_id",
                table: "product_reviews",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_reviews_store_product_id",
                table: "product_reviews",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ux_product_reviews_farmer_profile_id_order_item_id",
                table: "product_reviews",
                columns: new[] { "farmer_profile_id", "order_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_brand_id",
                table: "products",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_status",
                table: "products",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_products_sku",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_items_agent_review_id",
                table: "recommendation_items",
                column: "agent_review_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_items_diagnosis_case_id",
                table: "recommendation_items",
                column: "diagnosis_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_items_disease_treatment_id",
                table: "recommendation_items",
                column: "disease_treatment_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_items_store_product_id",
                table: "recommendation_items",
                column: "store_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_original_payment_id",
                table: "refunds",
                column: "original_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_sales_return_id",
                table: "refunds",
                column: "sales_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_status_requested_at",
                table: "refunds",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ux_refunds_store_id_refund_number",
                table: "refunds",
                columns: new[] { "store_id", "refund_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_roles_code",
                table: "roles",
                column: "code",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_debt_adjustment_transaction_id",
                table: "sales_return_items",
                column: "debt_adjustment_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_delivery_item_id",
                table: "sales_return_items",
                column: "delivery_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_delivery_item_lot_allocation_id",
                table: "sales_return_items",
                column: "delivery_item_lot_allocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_inventory_lot_id",
                table: "sales_return_items",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_order_item_id",
                table: "sales_return_items",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_original_stock_movement_item_id",
                table: "sales_return_items",
                column: "original_stock_movement_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_return_stock_movement_id",
                table: "sales_return_items",
                column: "return_stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_return_items_sales_return_id",
                table: "sales_return_items",
                column: "sales_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_farmer_profile_id_requested_at",
                table: "sales_returns",
                columns: new[] { "farmer_profile_id", "requested_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_order_id_requested_at",
                table: "sales_returns",
                columns: new[] { "order_id", "requested_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_sales_returns_status_requested_at",
                table: "sales_returns",
                columns: new[] { "status", "requested_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_sales_returns_store_id_return_number",
                table: "sales_returns",
                columns: new[] { "store_id", "return_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_items_inventory_lot_id",
                table: "stock_movement_items",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_items_stock_movement_id",
                table: "stock_movement_items",
                column: "stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_delivery_id",
                table: "stock_movements",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_goods_receipt_id",
                table: "stock_movements",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_movement_type_occurred_at",
                table: "stock_movements",
                columns: new[] { "movement_type", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_order_id",
                table: "stock_movements",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reversal_of_movement_id",
                table: "stock_movements",
                column: "reversal_of_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_sales_return_id",
                table: "stock_movements",
                column: "sales_return_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_stocktake_id",
                table: "stock_movements",
                column: "stocktake_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_store_id_occurred_at",
                table: "stock_movements",
                columns: new[] { "store_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_stocktake_items_inventory_lot_id",
                table: "stocktake_items",
                column: "inventory_lot_id");

            migrationBuilder.CreateIndex(
                name: "ux_stocktake_items_stocktake_id_inventory_lot_id",
                table: "stocktake_items",
                columns: new[] { "stocktake_id", "inventory_lot_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_store_id_created_at",
                table: "stocktakes",
                columns: new[] { "store_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_stocktakes_store_id_status",
                table: "stocktakes",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_stocktakes_store_id_stocktake_number",
                table: "stocktakes",
                columns: new[] { "store_id", "stocktake_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_members_store_id_status",
                table: "store_members",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_store_members_user_id_status",
                table: "store_members",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_store_members_store_id_user_id",
                table: "store_members",
                columns: new[] { "store_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_products_product_id",
                table: "store_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_products_store_id_is_active_is_sellable",
                table: "store_products",
                columns: new[] { "store_id", "is_active", "is_sellable" });

            migrationBuilder.CreateIndex(
                name: "ux_store_products_store_id_product_id",
                table: "store_products",
                columns: new[] { "store_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_stores_code",
                table: "stores",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_store_id_name",
                table: "suppliers",
                columns: new[] { "store_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ux_suppliers_store_id_code",
                table: "suppliers",
                columns: new[] { "store_id", "code" },
                unique: true,
                filter: "code IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_units_code",
                table: "units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_addresses_user_id",
                table: "user_addresses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_addresses_default",
                table: "user_addresses",
                column: "user_id",
                unique: true,
                filter: "is_default AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "deleted_at IS NULL AND phone_number IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_active_ingredients_deleted_by",
                table: "active_ingredients",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_ai_disease_id_snapshot",
                table: "agent_reviews",
                column: "ai_disease_id_snapshot",
                principalTable: "diseases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_final_disease_id",
                table: "agent_reviews",
                column: "final_disease_id",
                principalTable: "diseases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_deleted_by",
                table: "agent_reviews",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_diagnosis_case_id",
                table: "agent_reviews",
                column: "diagnosis_case_id",
                principalTable: "diagnosis_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_primary_ai_inference_id",
                table: "agent_reviews",
                column: "primary_ai_inference_id",
                principalTable: "ai_inferences",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_reviews_reviewer_member_id",
                table: "agent_reviews",
                column: "reviewer_member_id",
                principalTable: "store_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_ai_model_id",
                table: "ai_inferences",
                column: "ai_model_id",
                principalTable: "ai_models",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_ai_policy_config_id",
                table: "ai_inferences",
                column: "ai_policy_config_id",
                principalTable: "ai_policy_configs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_deleted_by",
                table: "ai_inferences",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_diagnosis_case_id",
                table: "ai_inferences",
                column: "diagnosis_case_id",
                principalTable: "diagnosis_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_diagnosis_image_id",
                table: "ai_inferences",
                column: "diagnosis_image_id",
                principalTable: "diagnosis_images",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_inferences_predicted_disease_id",
                table: "ai_inferences",
                column: "predicted_disease_id",
                principalTable: "diseases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ai_models_created_by",
                table: "ai_models",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_models_deleted_by",
                table: "ai_models",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_ai_policy_configs_created_by",
                table: "ai_policy_configs",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ai_policy_configs_deleted_by",
                table: "ai_policy_configs",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_articles_author_id",
                table: "articles",
                column: "author_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_articles_deleted_by",
                table: "articles",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_store_id",
                table: "audit_logs",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_brands_deleted_by",
                table: "brands",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_cart_id",
                table: "cart_items",
                column: "cart_id",
                principalTable: "carts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_deleted_by",
                table: "cart_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_product_packaging_id",
                table: "cart_items",
                column: "product_packaging_id",
                principalTable: "product_packagings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_store_product_id",
                table: "cart_items",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_carts_converted_order_id",
                table: "carts",
                column: "converted_order_id",
                principalTable: "orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_carts_deleted_by",
                table: "carts",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_carts_farmer_profile_id",
                table: "carts",
                column: "farmer_profile_id",
                principalTable: "farmer_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_carts_store_id",
                table: "carts",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_categories_deleted_by",
                table: "categories",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_contact_requests_assigned_to",
                table: "contact_requests",
                column: "assigned_to",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_contact_requests_deleted_by",
                table: "contact_requests",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_contact_requests_user_id",
                table: "contact_requests",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_limit_histories_changed_by",
                table: "credit_limit_histories",
                column: "changed_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_limit_histories_deleted_by",
                table: "credit_limit_histories",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_limit_histories_farmer_credit_profile_id",
                table: "credit_limit_histories",
                column: "farmer_credit_profile_id",
                principalTable: "farmer_credit_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_limit_histories_new_credit_tier_id",
                table: "credit_limit_histories",
                column: "new_credit_tier_id",
                principalTable: "credit_tiers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_limit_histories_old_credit_tier_id",
                table: "credit_limit_histories",
                column: "old_credit_tier_id",
                principalTable: "credit_tiers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_deleted_by",
                table: "credit_reservations",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_released_by",
                table: "credit_reservations",
                column: "released_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_reserved_by",
                table: "credit_reservations",
                column: "reserved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_farmer_credit_profile_id",
                table: "credit_reservations",
                column: "farmer_credit_profile_id",
                principalTable: "farmer_credit_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_order_id",
                table: "credit_reservations",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_reservations_store_id",
                table: "credit_reservations",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_credit_tiers_deleted_by",
                table: "credit_tiers",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_credit_tiers_store_id",
                table: "credit_tiers",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_assignments_assigned_by",
                table: "customer_group_assignments",
                column: "assigned_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_assignments_deleted_by",
                table: "customer_group_assignments",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_assignments_customer_group_id",
                table: "customer_group_assignments",
                column: "customer_group_id",
                principalTable: "customer_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_assignments_farmer_profile_id",
                table: "customer_group_assignments",
                column: "farmer_profile_id",
                principalTable: "farmer_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_price_lists_assigned_by",
                table: "customer_group_price_lists",
                column: "assigned_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_price_lists_deleted_by",
                table: "customer_group_price_lists",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_price_lists_customer_group_id",
                table: "customer_group_price_lists",
                column: "customer_group_id",
                principalTable: "customer_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_group_price_lists_price_list_id",
                table: "customer_group_price_lists",
                column: "price_list_id",
                principalTable: "price_lists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customer_groups_deleted_by",
                table: "customer_groups",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_groups_store_id",
                table: "customer_groups",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_debt_accounts_deleted_by",
                table: "debt_accounts",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_accounts_farmer_profile_id",
                table: "debt_accounts",
                column: "farmer_profile_id",
                principalTable: "farmer_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_debt_accounts_store_id",
                table: "debt_accounts",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_created_by",
                table: "debt_entries",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_deleted_by",
                table: "debt_entries",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_delivery_attempt_id",
                table: "debt_entries",
                column: "delivery_attempt_id",
                principalTable: "delivery_attempts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_delivery_id",
                table: "debt_entries",
                column: "delivery_id",
                principalTable: "deliveries",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_order_id",
                table: "debt_entries",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entries_source_stock_movement_id",
                table: "debt_entries",
                column: "source_stock_movement_id",
                principalTable: "stock_movements",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entry_actions_created_by",
                table: "debt_entry_actions",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_debt_entry_actions_deleted_by",
                table: "debt_entry_actions",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_transactions_created_by",
                table: "debt_transactions",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_transactions_deleted_by",
                table: "debt_transactions",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_transactions_payment_allocation_id",
                table: "debt_transactions",
                column: "payment_allocation_id",
                principalTable: "payment_allocations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_debt_transactions_sales_return_id",
                table: "debt_transactions",
                column: "sales_return_id",
                principalTable: "sales_returns",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_assigned_to_member_id",
                table: "deliveries",
                column: "assigned_to_member_id",
                principalTable: "store_members",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_cancelled_by",
                table: "deliveries",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_created_by",
                table: "deliveries",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_deleted_by",
                table: "deliveries",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_order_id",
                table: "deliveries",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_deliveries_store_id",
                table: "deliveries",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempt_items_deleted_by",
                table: "delivery_attempt_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempt_items_delivery_attempt_id",
                table: "delivery_attempt_items",
                column: "delivery_attempt_id",
                principalTable: "delivery_attempts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempt_items_delivery_item_lot_allocation_id",
                table: "delivery_attempt_items",
                column: "delivery_item_lot_allocation_id",
                principalTable: "delivery_item_lot_allocations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempts_attempted_by_member_id",
                table: "delivery_attempts",
                column: "attempted_by_member_id",
                principalTable: "store_members",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempts_deleted_by",
                table: "delivery_attempts",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_attempts_sale_stock_movement_id",
                table: "delivery_attempts",
                column: "sale_stock_movement_id",
                principalTable: "stock_movements",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_incidents_deleted_by",
                table: "delivery_incidents",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_incidents_reported_by",
                table: "delivery_incidents",
                column: "reported_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_incidents_resolved_by",
                table: "delivery_incidents",
                column: "resolved_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_incidents_delivery_item_lot_allocation_id",
                table: "delivery_incidents",
                column: "delivery_item_lot_allocation_id",
                principalTable: "delivery_item_lot_allocations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_incidents_related_stock_movement_id",
                table: "delivery_incidents",
                column: "related_stock_movement_id",
                principalTable: "stock_movements",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_item_lot_allocations_deleted_by",
                table: "delivery_item_lot_allocations",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_item_lot_allocations_delivery_item_id",
                table: "delivery_item_lot_allocations",
                column: "delivery_item_id",
                principalTable: "delivery_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_item_lot_allocations_inventory_lot_id",
                table: "delivery_item_lot_allocations",
                column: "inventory_lot_id",
                principalTable: "inventory_lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_item_lot_allocations_inventory_reservation_item_id",
                table: "delivery_item_lot_allocations",
                column: "inventory_reservation_item_id",
                principalTable: "inventory_reservation_items",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_items_deleted_by",
                table: "delivery_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_delivery_items_order_item_id",
                table: "delivery_items",
                column: "order_item_id",
                principalTable: "order_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_diagnosis_cases_deleted_by",
                table: "diagnosis_cases",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_diagnosis_cases_farmer_profile_id",
                table: "diagnosis_cases",
                column: "farmer_profile_id",
                principalTable: "farmer_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_diagnosis_cases_final_disease_id",
                table: "diagnosis_cases",
                column: "final_disease_id",
                principalTable: "diseases",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_diagnosis_images_deleted_by",
                table: "diagnosis_images",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_disease_treatments_deleted_by",
                table: "disease_treatments",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_disease_treatments_disease_id",
                table: "disease_treatments",
                column: "disease_id",
                principalTable: "diseases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_diseases_deleted_by",
                table: "diseases",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_credit_profiles_approved_by",
                table: "farmer_credit_profiles",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_credit_profiles_deleted_by",
                table: "farmer_credit_profiles",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_credit_profiles_farmer_profile_id",
                table: "farmer_credit_profiles",
                column: "farmer_profile_id",
                principalTable: "farmer_profiles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_credit_profiles_store_id",
                table: "farmer_credit_profiles",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_profiles_deleted_by",
                table: "farmer_profiles",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_farmer_profiles_user_id",
                table: "farmer_profiles",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_items_deleted_by",
                table: "goods_receipt_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_items_goods_receipt_id",
                table: "goods_receipt_items",
                column: "goods_receipt_id",
                principalTable: "goods_receipts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_items_inventory_lot_id",
                table: "goods_receipt_items",
                column: "inventory_lot_id",
                principalTable: "inventory_lots",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_items_product_packaging_id",
                table: "goods_receipt_items",
                column: "product_packaging_id",
                principalTable: "product_packagings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipt_items_store_product_id",
                table: "goods_receipt_items",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_cancelled_by",
                table: "goods_receipts",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_confirmed_by",
                table: "goods_receipts",
                column: "confirmed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_deleted_by",
                table: "goods_receipts",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_received_by",
                table: "goods_receipts",
                column: "received_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_store_id",
                table: "goods_receipts",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_goods_receipts_supplier_id",
                table: "goods_receipts",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_lot_balances_deleted_by",
                table: "inventory_lot_balances",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_lot_balances_inventory_lot_id",
                table: "inventory_lot_balances",
                column: "inventory_lot_id",
                principalTable: "inventory_lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_lots_deleted_by",
                table: "inventory_lots",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_lots_store_product_id",
                table: "inventory_lots",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservation_items_deleted_by",
                table: "inventory_reservation_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservation_items_inventory_reservation_id",
                table: "inventory_reservation_items",
                column: "inventory_reservation_id",
                principalTable: "inventory_reservations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservation_items_order_item_id",
                table: "inventory_reservation_items",
                column: "order_item_id",
                principalTable: "order_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservations_deleted_by",
                table: "inventory_reservations",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservations_released_by",
                table: "inventory_reservations",
                column: "released_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservations_reserved_by",
                table: "inventory_reservations",
                column: "reserved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservations_order_id",
                table: "inventory_reservations",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_reservations_store_id",
                table: "inventory_reservations",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_deleted_by",
                table: "notifications",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_user_id",
                table: "notifications",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_deleted_by",
                table: "order_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_overridden_by",
                table: "order_items",
                column: "overridden_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_order_id",
                table: "order_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_product_packaging_id",
                table: "order_items",
                column: "product_packaging_id",
                principalTable: "product_packagings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_store_product_id",
                table: "order_items",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_cancelled_by",
                table: "orders",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_confirmed_by",
                table: "orders",
                column: "confirmed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_created_by",
                table: "orders",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_deleted_by",
                table: "orders",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_pickup_completed_by",
                table: "orders",
                column: "pickup_completed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_price_list_id_snapshot",
                table: "orders",
                column: "price_list_id_snapshot",
                principalTable: "price_lists",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_source_address_id",
                table: "orders",
                column: "source_address_id",
                principalTable: "user_addresses",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_store_id",
                table: "orders",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_allocated_by",
                table: "payment_allocations",
                column: "allocated_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_deleted_by",
                table: "payment_allocations",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_reversed_by",
                table: "payment_allocations",
                column: "reversed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_payment_id",
                table: "payment_allocations",
                column: "payment_id",
                principalTable: "payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_confirmed_by",
                table: "payments",
                column: "confirmed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_created_by",
                table: "payments",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_deleted_by",
                table: "payments",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_payments_store_id",
                table: "payments",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_price_list_items_deleted_by",
                table: "price_list_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_price_list_items_price_list_id",
                table: "price_list_items",
                column: "price_list_id",
                principalTable: "price_lists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_price_list_items_product_packaging_id",
                table: "price_list_items",
                column: "product_packaging_id",
                principalTable: "product_packagings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_price_list_items_store_product_id",
                table: "price_list_items",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_price_lists_deleted_by",
                table: "price_lists",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_price_lists_store_id",
                table: "price_lists",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_active_ingredients_deleted_by",
                table: "product_active_ingredients",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_product_active_ingredients_product_id",
                table: "product_active_ingredients",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_packagings_deleted_by",
                table: "product_packagings",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_product_packagings_product_id",
                table: "product_packagings",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_packagings_unit_id",
                table: "product_packagings",
                column: "unit_id",
                principalTable: "units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_product_reviews_deleted_by",
                table: "product_reviews",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_product_reviews_store_product_id",
                table: "product_reviews",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_deleted_by",
                table: "products",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_recommendation_items_approved_by",
                table: "recommendation_items",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_recommendation_items_deleted_by",
                table: "recommendation_items",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_recommendation_items_store_product_id",
                table: "recommendation_items",
                column: "store_product_id",
                principalTable: "store_products",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_cancelled_by",
                table: "refunds",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_completed_by",
                table: "refunds",
                column: "completed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_deleted_by",
                table: "refunds",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_requested_by",
                table: "refunds",
                column: "requested_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_sales_return_id",
                table: "refunds",
                column: "sales_return_id",
                principalTable: "sales_returns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_refunds_store_id",
                table: "refunds",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_roles_deleted_by",
                table: "roles",
                column: "deleted_by",
                principalTable: "users",
                principalColumn: "id");

            // Expression indexes EF Core cannot model (database design §35.14); SQL kept in PostgreSqlRawIndexes.
            migrationBuilder.Sql(PostgreSqlRawIndexes.InventoryLotsLogicalLot);
            migrationBuilder.Sql(PostgreSqlRawIndexes.UsersEmailLower);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PostgreSqlRawIndexes.DropUsersEmailLower);
            migrationBuilder.Sql(PostgreSqlRawIndexes.DropInventoryLotsLogicalLot);

            migrationBuilder.DropForeignKey(
                name: "fk_roles_deleted_by",
                table: "roles");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "cart_items");

            migrationBuilder.DropTable(
                name: "contact_requests");

            migrationBuilder.DropTable(
                name: "credit_limit_histories");

            migrationBuilder.DropTable(
                name: "credit_reservations");

            migrationBuilder.DropTable(
                name: "customer_group_assignments");

            migrationBuilder.DropTable(
                name: "customer_group_price_lists");

            migrationBuilder.DropTable(
                name: "delivery_attempt_items");

            migrationBuilder.DropTable(
                name: "delivery_incidents");

            migrationBuilder.DropTable(
                name: "goods_receipt_items");

            migrationBuilder.DropTable(
                name: "inventory_lot_balances");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "price_list_items");

            migrationBuilder.DropTable(
                name: "product_active_ingredients");

            migrationBuilder.DropTable(
                name: "product_reviews");

            migrationBuilder.DropTable(
                name: "recommendation_items");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "sales_return_items");

            migrationBuilder.DropTable(
                name: "stocktake_items");

            migrationBuilder.DropTable(
                name: "carts");

            migrationBuilder.DropTable(
                name: "farmer_credit_profiles");

            migrationBuilder.DropTable(
                name: "agent_reviews");

            migrationBuilder.DropTable(
                name: "disease_treatments");

            migrationBuilder.DropTable(
                name: "debt_transactions");

            migrationBuilder.DropTable(
                name: "delivery_item_lot_allocations");

            migrationBuilder.DropTable(
                name: "stock_movement_items");

            migrationBuilder.DropTable(
                name: "credit_tiers");

            migrationBuilder.DropTable(
                name: "ai_inferences");

            migrationBuilder.DropTable(
                name: "active_ingredients");

            migrationBuilder.DropTable(
                name: "debt_entry_actions");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "delivery_items");

            migrationBuilder.DropTable(
                name: "inventory_reservation_items");

            migrationBuilder.DropTable(
                name: "ai_policy_configs");

            migrationBuilder.DropTable(
                name: "diagnosis_images");

            migrationBuilder.DropTable(
                name: "debt_entries");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "inventory_lots");

            migrationBuilder.DropTable(
                name: "inventory_reservations");

            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "ai_models");

            migrationBuilder.DropTable(
                name: "diagnosis_cases");

            migrationBuilder.DropTable(
                name: "debt_accounts");

            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "product_packagings");

            migrationBuilder.DropTable(
                name: "store_products");

            migrationBuilder.DropTable(
                name: "diseases");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "units");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "goods_receipts");

            migrationBuilder.DropTable(
                name: "sales_returns");

            migrationBuilder.DropTable(
                name: "stocktakes");

            migrationBuilder.DropTable(
                name: "brands");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "store_members");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "customer_groups");

            migrationBuilder.DropTable(
                name: "farmer_profiles");

            migrationBuilder.DropTable(
                name: "price_lists");

            migrationBuilder.DropTable(
                name: "user_addresses");

            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
