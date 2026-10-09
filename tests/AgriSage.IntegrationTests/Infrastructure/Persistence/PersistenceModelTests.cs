using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Verifies the EF Core model against the database design without connecting to PostgreSQL.
public class PersistenceModelTests
{
    private static readonly string[] DocumentedTables =
    [
        "roles", "users", "farmer_profiles", "user_addresses", "stores", "store_members",
        "customer_groups", "customer_group_assignments",
        "categories", "brands", "products", "units", "product_packagings", "active_ingredients",
        "product_active_ingredients", "store_products", "product_reviews",
        "price_lists", "price_list_items", "customer_group_price_lists",
        "suppliers", "goods_receipts", "goods_receipt_items",
        "inventory_lots", "inventory_lot_balances", "stock_movements", "stock_movement_items",
        "stocktakes", "stocktake_items", "inventory_reservations", "inventory_reservation_items",
        "carts", "cart_items", "orders", "order_items", "payments", "payment_allocations",
        "deliveries", "delivery_items", "delivery_item_lot_allocations", "delivery_attempts",
        "delivery_attempt_items", "delivery_incidents",
        "credit_tiers", "farmer_credit_profiles", "credit_limit_histories", "credit_reservations",
        "debt_accounts", "debt_entries", "debt_entry_actions", "debt_transactions",
        "sales_returns", "sales_return_items", "refunds",
        "diseases", "disease_treatments", "ai_models", "ai_policy_configs", "diagnosis_cases",
        "diagnosis_images", "ai_inferences", "agent_reviews", "recommendation_items",
        "articles", "contact_requests", "notifications", "audit_logs",
        "auth_sessions", "refresh_tokens", "auth_challenges", "notification_outbox"
    ];

    private static readonly (IModel DesignModel, IModel RuntimeModel) Models = BuildModels();

    private static IModel Model => Models.DesignModel;

    private static (IModel, IModel) BuildModels()
    {
        var options = new DbContextOptionsBuilder<AgriSageDbContext>()
            .UseNpgsql("Host=localhost;Database=agrisage_model_only")
            .Options;

        using var context = new AgriSageDbContext(options);
        return (context.GetService<IDesignTimeModel>().Model, context.Model);
    }

    private static IEntityType Table(string name) =>
        Model.GetEntityTypes().Single(entityType => entityType.GetTableName() == name);

    private static ICheckConstraint Check(string table, string name) =>
        Table(table).GetCheckConstraints().Single(check => check.Name == $"ck_{table}_{name}");

    [Fact]
    public void Model_maps_exactly_the_71_documented_tables()
    {
        var tables = Model.GetEntityTypes().Select(entityType => entityType.GetTableName()).ToList();

        Assert.Equal(71, DocumentedTables.Length);
        Assert.Equal(DocumentedTables.Order(), tables.Order());
        Assert.DoesNotContain(Model.GetEntityTypes(), entityType => entityType.IsOwned());
    }

    [Fact]
    public void Every_table_has_a_uuid_id_primary_key()
    {
        Assert.All(Model.GetEntityTypes(), entityType =>
        {
            var key = entityType.FindPrimaryKey();
            Assert.NotNull(key);
            var property = Assert.Single(key.Properties);
            Assert.Equal("id", property.GetColumnName());
            Assert.Equal(typeof(Guid), property.ClrType);
            Assert.Equal("gen_random_uuid()", property.GetDefaultValueSql());
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        });
    }

    [Fact]
    public void Only_audit_logs_lacks_audit_and_soft_delete_columns()
    {
        var withoutSoftDelete = Model.GetEntityTypes()
            .Where(entityType => entityType.FindProperty(nameof(SoftDeletableEntity.DeletedAt)) is null)
            .Select(entityType => entityType.GetTableName());

        Assert.Equal(["audit_logs"], withoutSoftDelete);
        Assert.Null(Table("audit_logs").FindProperty(nameof(AuditableEntity.CreatedAt)));
    }

    [Fact]
    public void No_relationship_cascades_or_sets_null()
    {
        var foreignKeys = Model.GetEntityTypes().SelectMany(entityType => entityType.GetForeignKeys()).ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(
            foreignKey.IsRequired ? DeleteBehavior.Restrict : DeleteBehavior.NoAction,
            foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Every_soft_deletable_table_references_users_through_deleted_by_without_an_index()
    {
        foreach (var entityType in Model.GetEntityTypes().Where(e => e.GetTableName() != "audit_logs"))
        {
            var deletedBy = entityType.FindProperty(nameof(SoftDeletableEntity.DeletedBy))!;

            Assert.Contains(entityType.GetForeignKeys(), foreignKey =>
                foreignKey.Properties.SequenceEqual([deletedBy])
                && foreignKey.PrincipalEntityType.GetTableName() == "users");
            Assert.DoesNotContain(entityType.GetIndexes(), index => index.Properties[0] == deletedBy);
        }
    }

    [Fact]
    public void Actor_columns_are_not_indexed_but_operational_user_columns_are()
    {
        Assert.DoesNotContain(Table("orders").GetIndexes(), index => index.Properties[0].GetColumnName() == "created_by");
        Assert.DoesNotContain(Table("goods_receipts").GetIndexes(), index => index.Properties[0].GetColumnName() == "received_by");
        Assert.Contains(Table("user_addresses").GetIndexes(), index => index.Properties[0].GetColumnName() == "user_id");
        Assert.Contains(Table("audit_logs").GetIndexes(), index => index.Properties[0].GetColumnName() == "actor_user_id");
        Assert.Contains(Table("contact_requests").GetIndexes(), index => index.Properties[0].GetColumnName() == "assigned_to");
    }

    [Fact]
    public void Every_operational_foreign_key_is_covered_by_an_unfiltered_index()
    {
        var operationalForeignKeys = Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType.GetTableName() != "users"
                || foreignKey.Properties[0].GetColumnName() is "user_id" or "actor_user_id");

        Assert.All(operationalForeignKeys, foreignKey => Assert.Contains(
            foreignKey.DeclaringEntityType.GetIndexes(),
            index => index.GetFilter() is null
                && index.Properties.Take(foreignKey.Properties.Count).SequenceEqual(foreignKey.Properties)));
    }

    // EF must materialize entities through the private parameterless constructor, never a public Domain
    // constructor (e.g. InventoryLot(...) would create a new Balance while reading from the database).
    [Fact]
    public void Entities_are_materialized_through_the_parameterless_constructor()
    {
        Assert.All(Models.RuntimeModel.GetEntityTypes(), entityType =>
        {
            // ConstructorBinding is not on a public interface in EF Core 10, so it is read via reflection.
            var binding = entityType.GetType().GetProperty("ConstructorBinding")!.GetValue(entityType)!;
            var parameters = (System.Collections.ICollection)binding.GetType().GetProperty("ParameterBindings")!.GetValue(binding)!;

            Assert.True(parameters.Count == 0, $"{entityType.DisplayName()} is materialized through a parameterized constructor.");
        });
    }

    [Fact]
    public void Columns_use_snake_case_names()
    {
        Assert.Equal("top_k", Table("ai_policy_configs").FindProperty("TopK")!.GetColumnName());
        Assert.Equal("ai_disease_id_snapshot", Table("agent_reviews").FindProperty("AiDiseaseIdSnapshot")!.GetColumnName());
        Assert.Equal("conversion_to_base_snapshot", Table("order_items").FindProperty("ConversionToBaseSnapshot")!.GetColumnName());
        Assert.Equal("delivery_address_line", Table("orders").FindProperty("DeliveryAddressLine")!.GetColumnName());
        Assert.All(
            Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()),
            property => Assert.Equal(property.GetColumnName(), property.GetColumnName().ToLowerInvariant()));
    }

    [Theory]
    [InlineData("order_items", "UnitPrice", 18, 2)]
    [InlineData("goods_receipt_items", "BaseUnitCost", 20, 6)]
    [InlineData("inventory_lot_balances", "TotalCostValue", 20, 6)]
    [InlineData("user_addresses", "Latitude", 10, 7)]
    [InlineData("ai_policy_configs", "MinimumConfidence", 5, 4)]
    [InlineData("ai_inferences", "Confidence", 7, 6)]
    [InlineData("debt_transactions", "AmountDelta", 18, 2)]
    public void Decimal_precision_follows_the_design(string table, string property, int precision, int scale)
    {
        var mapped = Table(table).FindProperty(property)!;

        Assert.Equal(precision, mapped.GetPrecision());
        Assert.Equal(scale, mapped.GetScale());
    }

    [Fact]
    public void Every_decimal_and_string_column_has_an_explicit_size_or_type()
    {
        var properties = Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()).ToList();

        Assert.All(properties.Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal)),
            property => Assert.NotNull(property.GetPrecision()));

        Assert.All(properties.Where(p => p.ClrType == typeof(string) || p.GetValueConverter() is not null), property =>
            Assert.True(
                property.GetMaxLength() is not null || property.GetColumnType() is "text" or "jsonb" or "char(3)",
                $"{property.DeclaringType.DisplayName()}.{property.Name} has no max length or explicit column type."));
    }

    [Fact]
    public void Text_json_and_currency_columns_use_their_postgresql_types()
    {
        Assert.Equal("text", Table("products").FindProperty("Description")!.GetColumnType());
        Assert.Equal("jsonb", Table("ai_models").FindProperty("ClassLabels")!.GetColumnType());
        Assert.Equal("jsonb", Table("audit_logs").FindProperty("NewValues")!.GetColumnType());
        Assert.Equal("char(3)", Table("payments").FindProperty("Currency")!.GetColumnType());
        Assert.Equal("smallint", Table("product_reviews").FindProperty("Rating")!.GetColumnType());
    }

    [Fact]
    public void Documented_defaults_exist_but_domain_values_are_always_written()
    {
        var isActive = Table("products").FindProperty("RequiresLotTracking")!;
        var topK = Table("ai_policy_configs").FindProperty("TopK")!;
        var currency = Table("payments").FindProperty("Currency")!;

        Assert.Equal(true, isActive.GetDefaultValue());
        Assert.Equal(ValueGenerated.Never, isActive.ValueGenerated);
        Assert.Equal(3, topK.GetDefaultValue());
        Assert.Equal("VND", currency.GetDefaultValue());
    }

    [Fact]
    public void Financial_inventory_versions_and_user_security_version_are_concurrency_tokens()
    {
        var tokens = Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.IsConcurrencyToken)
            .Select(property => $"{property.DeclaringType.GetTableName()}.{property.GetColumnName()}");

        Assert.Equal(
            ["debt_accounts.version", "farmer_credit_profiles.version", "inventory_lot_balances.version", "orders.version", "users.security_version"],
            tokens.Order());
        Assert.All(Model.GetEntityTypes().Where(entityType => entityType.ClrType.IsAssignableTo(typeof(IHasConcurrencyVersion))),
            entityType => Assert.Equal(ValueGenerated.Never, entityType.FindProperty("Version")!.ValueGenerated));
        Assert.False(Table("ai_models").FindProperty("Version")!.IsConcurrencyToken);
        Assert.False(Table("ai_policy_configs").FindProperty("Version")!.IsConcurrencyToken);
    }

    [Fact]
    public void Every_soft_deletable_table_filters_deleted_rows_and_audit_logs_has_no_filter()
    {
        Assert.All(Model.GetEntityTypes(), entityType =>
        {
            var filters = entityType.GetDeclaredQueryFilters();

            if (entityType.GetTableName() == "audit_logs")
            {
                Assert.Empty(filters);
                return;
            }

            var filter = Assert.Single(filters);
            Assert.Equal("entity => (entity.DeletedAt == null)", filter.Expression!.ToString());
        });
    }

    [Fact]
    public void Enum_columns_have_a_check_of_their_documented_values()
    {
        Assert.Equal(
            "status IN ('PENDING_CONFIRMATION', 'CONFIRMED', 'PREPARING', 'READY_FOR_FULFILLMENT', "
            + "'PARTIALLY_FULFILLED', 'COMPLETED', 'CANCELLED', 'PARTIALLY_CANCELLED')",
            Check("orders", "status").Sql);
        Assert.Equal("payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER')", Check("payments", "payment_method").Sql);
        Assert.Equal("confirmation_source IN ('STAFF', 'PAYOS_WEBHOOK')", Check("payments", "confirmation_source").Sql);
    }

    [Fact]
    public void Enum_properties_and_nullable_enum_properties_are_converted_to_documented_strings()
    {
        var method = Table("payments").FindProperty("PaymentMethod")!.GetValueConverter()!;
        var source = Table("payments").FindProperty("ConfirmationSource")!.GetValueConverter()!;

        Assert.Equal("PAYOS", method.ConvertToProvider(PaymentMethod.PayOs));
        Assert.Equal("PAYOS_WEBHOOK", source.ConvertToProvider(PaymentConfirmationSource.PayOsWebhook));
        Assert.Equal(PaymentMethod.Cash, method.ConvertFromProvider("CASH"));
        Assert.All(
            Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties())
                .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum),
            property => Assert.NotNull(property.GetValueConverter()));
    }

    [Theory]
    [InlineData("delivery_attempt_items", "delivered_failed_within_attempted",
        "delivered_base_quantity + failed_base_quantity <= attempted_base_quantity")]
    [InlineData("sales_return_items", "single_fulfillment_source",
        "(delivery_item_lot_allocation_id IS NULL) <> (original_stock_movement_item_id IS NULL)")]
    [InlineData("users", "contact",
        "NULLIF(BTRIM(email), '') IS NOT NULL OR NULLIF(BTRIM(phone_number), '') IS NOT NULL")]
    [InlineData("inventory_lot_balances", "zero_quantity_zero_cost", "quantity_on_hand > 0 OR total_cost_value = 0")]
    [InlineData("inventory_lot_balances", "reserved_within_on_hand", "quantity_reserved <= quantity_on_hand")]
    [InlineData("debt_transactions", "payment_allocation_source",
        "transaction_type <> 'PAYMENT' OR payment_allocation_id IS NOT NULL")]
    public void Critical_check_constraints_are_configured(string table, string name, string sql)
    {
        Assert.Equal(sql, Check(table, name).Sql);
    }

    [Fact]
    public void Attempt_item_check_does_not_require_equality()
    {
        Assert.DoesNotContain(
            Table("delivery_attempt_items").GetCheckConstraints(),
            check => check.Sql.Contains("= attempted_base_quantity") && !check.Sql.Contains("<="));
    }

    [Theory]
    [InlineData("inventory_lots", "ux_inventory_lots_no_lot_bucket", "lot_number IS NULL AND deleted_at IS NULL")]
    [InlineData("inventory_reservation_items", "ux_inventory_reservation_item_lot", "deleted_at IS NULL")]
    [InlineData("delivery_attempt_items", "ux_delivery_attempt_allocation", "deleted_at IS NULL")]
    [InlineData("diagnosis_images", "ux_diagnosis_images_primary", "is_primary = TRUE AND deleted_at IS NULL")]
    [InlineData("agent_reviews", "ux_agent_reviews_current", "is_current = TRUE AND deleted_at IS NULL")]
    [InlineData("customer_group_assignments", "ux_customer_group_assignments_current", "effective_to IS NULL AND deleted_at IS NULL")]
    [InlineData("product_packagings", "ux_product_packagings_base_unit", "is_base_unit AND deleted_at IS NULL")]
    [InlineData("inventory_reservations", "ux_inventory_reservations_open_order",
        "status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL")]
    [InlineData("credit_reservations", "ux_credit_reservations_open_order",
        "status IN ('ACTIVE', 'PARTIALLY_CONSUMED') AND deleted_at IS NULL")]
    [InlineData("price_lists", "ux_price_lists_walk_in_default",
        "is_walk_in_default AND status = 'ACTIVE' AND deleted_at IS NULL")]
    public void Documented_partial_unique_indexes_exist(string table, string indexName, string filter)
    {
        var index = Table(table).GetIndexes().Single(index => index.GetDatabaseName() == indexName);

        Assert.True(index.IsUnique);
        Assert.Equal(filter, index.GetFilter());
    }

    [Fact]
    public void Temporal_rules_are_not_simple_unique_indexes()
    {
        Assert.DoesNotContain(Table("customer_group_price_lists").GetIndexes(), index => index.IsUnique);
        Assert.DoesNotContain(
            Table("ai_policy_configs").GetIndexes(),
            index => index.IsUnique && index.Properties.Any(p => p.GetColumnName() == "effective_from"));
    }

    [Fact]
    public void Index_and_constraint_names_are_unique_within_the_model()
    {
        var names = Model.GetEntityTypes().SelectMany(entityType =>
                entityType.GetIndexes().Select(index => index.GetDatabaseName()!)
                    .Concat(entityType.GetForeignKeys().Select(foreignKey => foreignKey.GetConstraintName()!))
                    .Concat(entityType.GetCheckConstraints().Select(check => check.Name!)))
            .ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        // ≤ 63 characters without EF's truncation marker ('~'), i.e. the configured name is the database name.
        Assert.All(names, name => Assert.Matches("^[a-z0-9_]{1,63}$", name));
    }

    [Fact]
    public void Expression_indexes_are_kept_as_documented_raw_sql()
    {
        Assert.Contains("lower(lot_number)", PostgreSqlRawIndexes.InventoryLotsLogicalLot);
        Assert.Contains("NULLS NOT DISTINCT", PostgreSqlRawIndexes.InventoryLotsLogicalLot);
        Assert.Contains("ON users (LOWER(email))", PostgreSqlRawIndexes.UsersEmailLower);
        Assert.Equal(2, PostgreSqlRawIndexes.All.Count);
    }

    [Fact]
    public void Enum_database_values_use_documented_upper_snake_case()
    {
        Assert.Equal(["CASH", "PAYOS", "BANK_TRANSFER"], EnumDbValue.AllValues(typeof(PaymentMethod)));
        Assert.Equal(["STAFF", "PAYOS_WEBHOOK"], EnumDbValue.AllValues(typeof(PaymentConfirmationSource)));
    }
}
