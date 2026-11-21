using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// AI_DIAGNOSIS.md 2.2: one new table for the AI provider (its API key only as ciphertext) and two admin-only permissions.
public class AiProviderSettingsMigrationTests
{
    public static readonly string[] NewCodes = ["AI_PROVIDER.READ", "AI_PROVIDER.MANAGE"];

    [Fact]
    public void Up_only_creates_the_settings_table_and_admin_permissions()
    {
        var migration = new AiProviderSettings();
        Assert.All(migration.UpOperations, o => Assert.True(
            o is CreateTableOperation or InsertDataOperation or CreateIndexOperation or SqlOperation,
            $"Unexpected operation {o.GetType().Name}"));

        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("ai_provider_settings", table.Name);
        Assert.Equal(
            ["id", "provider", "model_id", "encrypted_api_key", "api_key_hint", "is_enabled", "timeout_seconds", "version",
             "created_at", "updated_at", "deleted_at", "deleted_by"],
            table.Columns.Select(c => c.Name));
        // The key column is ciphertext of any length; the hint is only the last 4 characters.
        Assert.Equal("text", table.Columns.Single(c => c.Name == "encrypted_api_key").ColumnType);
        Assert.Equal(4, table.Columns.Single(c => c.Name == "api_key_hint").MaxLength);
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_ai_provider_settings_enabled_needs_key");
        Assert.Contains(table.CheckConstraints, c => c.Name == "ck_ai_provider_settings_provider" && c.Sql == "provider IN ('ANTHROPIC', 'GOOGLE')");

        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.Equal("deleted_at IS NULL", index.Filter);

        var insert = Assert.Single(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        Assert.Equal(2, insert.Values.GetLength(0));
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var definition in PermissionCatalog.Entries.Where(p => p.Module == "AI_PROVIDER"))
        {
            Assert.Equal(["ADMIN"], definition.DefaultRoles);
            Assert.False(definition.Delegable);
            Assert.Contains($"('ADMIN', '{definition.Id}'::uuid)", sql);
        }

        Assert.DoesNotContain("STORE_OWNER", sql);
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
    }

    [Fact]
    public void Down_removes_permission_references_before_the_permissions()
    {
        var operations = new AiProviderSettings().DownOperations;
        var cleanup = Assert.IsType<SqlOperation>(operations[0]).Sql;
        Assert.Contains("DELETE FROM store_member_permissions", cleanup);
        Assert.Contains("DELETE FROM role_permissions", cleanup);
        Assert.IsType<DropTableOperation>(operations[1]);
        Assert.Equal(2, operations.OfType<DeleteDataOperation>().Count());
    }
}
