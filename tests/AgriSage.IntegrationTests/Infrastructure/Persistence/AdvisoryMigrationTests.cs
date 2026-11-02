using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Review of AdvisoryProblemProducts (api-flows/ADVISORY.md): two columns on diseases (with a default, so existing rows are
// valid), three new tables, the permission rows and their role defaults. Nothing is dropped, altered or rewritten.
public sealed class AdvisoryMigrationTests
{
    // The codes this migration adds; the migrations shipped before it exclude them from the catalog they are compared with.
    public static readonly string[] NewCodes =
    [
        "MY_ADVISORY.READ", "DISEASES.CREATE", "ADVISORY_SESSIONS.READ",
        "DISEASE_PRODUCTS.READ", "DISEASE_PRODUCTS.CREATE", "DISEASE_PRODUCTS.UPDATE", "DISEASE_PRODUCTS.APPROVE",
        "DISEASE_PRODUCTS.DEACTIVATE"
    ];

    [Fact]
    public void Up_adds_columns_tables_and_permissions_without_destructive_operations()
    {
        var ops = new AdvisoryProblemProducts().UpOperations;

        Assert.DoesNotContain(ops, o => o is DropTableOperation or DropColumnOperation or AlterColumnOperation
            or DropIndexOperation or DropForeignKeyOperation or DeleteDataOperation or UpdateDataOperation or RenameColumnOperation);

        var columns = ops.OfType<AddColumnOperation>().ToList();
        Assert.Equal(["problem_type", "search_keywords"], columns.Select(c => c.Name).Order());
        Assert.All(columns, c => Assert.Equal("diseases", c.Table));
        var problemType = columns.Single(c => c.Name == "problem_type");
        Assert.False(problemType.IsNullable);
        Assert.Equal("DISEASE", problemType.DefaultValue);

        Assert.Equal(
            ["advisory_session_items", "advisory_sessions", "disease_products"],
            ops.OfType<CreateTableOperation>().Select(t => t.Name).Order());
    }

    [Fact]
    public void Business_foreign_keys_restrict_deletion_and_a_link_is_unique_per_problem_and_product()
    {
        var ops = new AdvisoryProblemProducts().UpOperations;

        var foreignKeys = ops.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys).ToList();
        Assert.All(
            foreignKeys.Where(f => f.Name is not null && !f.Name.EndsWith("_deleted_by", StringComparison.Ordinal)
                && !f.Name.EndsWith("_approved_by", StringComparison.Ordinal) && !f.Name.EndsWith("_diagnosis_case_id", StringComparison.Ordinal)),
            f => Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, f.OnDelete));
        Assert.DoesNotContain(foreignKeys, f => f.OnDelete == Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade);

        var unique = Assert.Single(ops.OfType<CreateIndexOperation>(), i => i.IsUnique);
        Assert.Equal("disease_products", unique.Table);
        Assert.Equal(["disease_id", "product_id"], unique.Columns);
        Assert.Equal("deleted_at IS NULL", unique.Filter);
    }

    [Fact]
    public void The_table_checks_enforce_what_the_domain_requires()
    {
        var links = new AdvisoryProblemProducts().UpOperations.OfType<CreateTableOperation>().Single(t => t.Name == "disease_products");
        var checks = links.CheckConstraints.Select(c => c.Sql).ToList();

        Assert.Contains("recommendation_group <> 'PROTECTION' OR registration_source IS NOT NULL", checks);
        Assert.Contains("recommendation_group <> 'NUTRITION' OR reason IS NOT NULL", checks);
        Assert.Contains("status <> 'APPROVED' OR (approved_by IS NOT NULL AND approved_at IS NOT NULL)", checks);
        Assert.Contains("applications BETWEEN 1 AND 10", checks);
        Assert.Contains("dose_per_ha_base IS NULL OR dose_per_ha_base > 0", checks);
        Assert.Contains("status IN ('DRAFT', 'APPROVED', 'INACTIVE')", checks);
    }

    [Fact]
    public void Permission_rows_and_role_defaults_match_the_catalog()
    {
        var ops = new AdvisoryProblemProducts().UpOperations;

        var insert = Assert.Single(ops.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        var codeColumn = Array.IndexOf(insert.Columns, "code");
        var inserted = Enumerable.Range(0, insert.Values.GetLength(0)).Select(i => (string)insert.Values[i, codeColumn]!).ToList();
        Assert.Equal(NewCodes.Order(), inserted.Order());

        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;
        Assert.Contains("INSERT INTO role_permissions", sql);
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);

        foreach (var code in NewCodes)
        {
            var permission = PermissionCatalog.ByCode[code];
            Assert.Contains($"('ADMIN', '{permission.Id}'::uuid)", sql);
            foreach (var role in permission.DefaultRoles)
            {
                Assert.Contains($"('{role}', '{permission.Id}'::uuid)", sql);
            }
        }

        Assert.Equal(["FARMER"], PermissionCatalog.ByCode["MY_ADVISORY.READ"].DefaultRoles);
        Assert.DoesNotContain("FARMER", PermissionCatalog.ByCode["DISEASE_PRODUCTS.APPROVE"].DefaultRoles);
        Assert.DoesNotContain("SALES_STAFF", PermissionCatalog.ByCode["DISEASE_PRODUCTS.APPROVE"].DefaultRoles);
    }

    [Fact]
    public void Down_removes_the_role_defaults_before_the_permissions_and_drops_the_tables_children_first()
    {
        var ops = new AdvisoryProblemProducts().DownOperations;

        var first = Assert.IsType<SqlOperation>(ops[0]);
        Assert.Contains("DELETE FROM role_permissions", first.Sql);
        Assert.Equal(NewCodes.Length, ops.OfType<DeleteDataOperation>().Count());

        var drops = ops.OfType<DropTableOperation>().Select(o => o.Name).ToList();
        Assert.True(drops.IndexOf("advisory_session_items") < drops.IndexOf("advisory_sessions"));
        Assert.True(drops.IndexOf("advisory_session_items") < drops.IndexOf("disease_products"));
    }
}
