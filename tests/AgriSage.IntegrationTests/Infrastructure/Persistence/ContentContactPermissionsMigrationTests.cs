using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class ContentContactPermissionsMigrationTests
{
    public static readonly string[] NewCodes = ["ARTICLES.READ", "ARTICLES.MANAGE", "CONTACT_REQUESTS.READ", "CONTACT_REQUESTS.MANAGE", "MY_CONTACT_REQUESTS.READ"];
    [Fact]
    public void Migration_only_adds_permissions_and_defaults_and_undo_cleans_references_first()
    {
        var migration = new ContentContactPermissions();
        Assert.All(migration.UpOperations, o => Assert.True(o is InsertDataOperation or SqlOperation));
        var insert = Assert.Single(migration.UpOperations.OfType<InsertDataOperation>());
        Assert.Equal("permissions", insert.Table);
        Assert.Equal(5, insert.Values.GetLength(0));
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        foreach (var definition in PermissionCatalog.Entries.Where(p => p.Module is "ARTICLES" or "CONTACT_REQUESTS" or "MY_CONTACT_REQUESTS"))
        {
            foreach (var role in definition.DefaultRoles.Append("ADMIN").Distinct())
                Assert.Contains($"('{role}', '{definition.Id}'::uuid)", sql);
        }
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        var cleanup = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("DELETE FROM role_permissions", cleanup);
        Assert.Contains("DELETE FROM store_member_permissions", cleanup);
        Assert.False(PermissionCatalog.ByCode["ARTICLES.MANAGE"].AllowsRole("SALES_STAFF"));
        Assert.DoesNotContain("FARMER", PermissionCatalog.ByCode["CONTACT_REQUESTS.READ"].DefaultRoles);
    }
}
