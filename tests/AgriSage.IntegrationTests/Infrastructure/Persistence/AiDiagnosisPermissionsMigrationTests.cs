using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Review of AiDiagnosisPermissions: data only. It adds the permission rows of the AI diagnosis APIs (the catalog is seeded
// with HasData) and their role defaults, and nothing else: no table, column, index or foreign key changes.
public sealed class AiDiagnosisPermissionsMigrationTests
{
    // The codes this migration adds; DynamicPermissionsMigrationTests excludes them from the catalog it shipped with.
    public static readonly string[] NewCodes =
    [
        "MY_DIAGNOSIS.CREATE", "MY_DIAGNOSIS.READ", "MY_DIAGNOSIS.CANCEL",
        "DIAGNOSIS.READ", "DIAGNOSIS.START_REVIEW", "DIAGNOSIS.REVIEW", "DIAGNOSIS.RERUN_AI", "DIAGNOSIS.RECOMMEND",
        "DIAGNOSIS.UNRECOMMEND",
        "AI_MODELS.READ", "AI_MODELS.CREATE", "AI_MODELS.ACTIVATE", "AI_MODELS.RETIRE",
        "AI_POLICIES.READ", "AI_POLICIES.CREATE", "AI_POLICIES.ACTIVATE", "AI_POLICIES.DEACTIVATE",
        "DISEASES.READ", "DISEASES.UPDATE",
        "DISEASE_TREATMENTS.CREATE", "DISEASE_TREATMENTS.UPDATE", "DISEASE_TREATMENTS.ACTIVATE", "DISEASE_TREATMENTS.DEACTIVATE",
        "STAFF.SET_AI_REVIEW"
    ];

    [Fact]
    public void Up_only_inserts_the_new_permission_rows_and_their_role_defaults()
    {
        var ops = new AiDiagnosisPermissions().UpOperations;

        Assert.DoesNotContain(ops, o => o is CreateTableOperation or DropTableOperation or AddColumnOperation or DropColumnOperation
            or AlterColumnOperation or CreateIndexOperation or DropIndexOperation or AddForeignKeyOperation
            or DeleteDataOperation or UpdateDataOperation);

        var insert = Assert.Single(ops.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        var codeColumn = Array.IndexOf(insert.Columns, "code");
        var inserted = Enumerable.Range(0, insert.Values.GetLength(0)).Select(i => (string)insert.Values[i, codeColumn]).ToList();
        Assert.Equal(NewCodes.Order(), inserted.Order());

        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;
        Assert.Contains("INSERT INTO role_permissions", sql);
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_new_permission_has_a_role_default_and_the_catalog_matches_the_migration()
    {
        var ops = new AiDiagnosisPermissions().UpOperations;
        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;

        foreach (var code in NewCodes)
        {
            var permission = PermissionCatalog.ByCode[code];
            Assert.Contains($"('ADMIN', '{permission.Id}'::uuid)", sql);
            foreach (var role in permission.DefaultRoles)
            {
                Assert.Contains($"('{role}', '{permission.Id}'::uuid)", sql);
            }
        }

        // Reviewer and admin screens of AI never go to delivery staff or Farmers by default.
        Assert.All(NewCodes.Where(c => c.StartsWith("DIAGNOSIS.", StringComparison.Ordinal)),
            code => Assert.DoesNotContain("FARMER", PermissionCatalog.ByCode[code].DefaultRoles));
        Assert.All(NewCodes.Where(c => c.StartsWith("MY_DIAGNOSIS.", StringComparison.Ordinal)),
            code => Assert.Equal(["FARMER"], PermissionCatalog.ByCode[code].DefaultRoles));
    }

    [Fact]
    public void Down_removes_the_role_defaults_before_the_permissions()
    {
        var ops = new AiDiagnosisPermissions().DownOperations;

        var first = Assert.IsType<SqlOperation>(ops[0]);
        Assert.Contains("DELETE FROM role_permissions", first.Sql);
        Assert.Equal(NewCodes.Length, ops.OfType<DeleteDataOperation>().Count());
        Assert.All(ops.OfType<DeleteDataOperation>(), o => Assert.Equal("permissions", o.Table));
    }
}
