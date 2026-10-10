using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;
public sealed class DynamicPermissionsMigrationTests
{
    [Fact]
    public void Migration_is_additive_restricts_foreign_keys_and_seeds_reviewed_defaults()
    {
        var ops = new DynamicPermissions().UpOperations;
        Assert.Equal(new[] { "permissions", "role_permissions", "store_member_permissions" }, ops.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.Equal(new[] { "roles.version", "store_members.version" }, ops.OfType<AddColumnOperation>().Select(c => $"{c.Table}.{c.Name}").Order());
        Assert.DoesNotContain(ops, o => o is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.All(ops.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        // The catalog as this migration shipped it: permissions added by later migrations are not part of it.
        var shipped = PermissionCatalog.Entries.Where(p => !AiDiagnosisPermissionsMigrationTests.NewCodes.Contains(p.Code)).ToList();
        var seed = Assert.Single(ops.OfType<InsertDataOperation>());
        Assert.Equal(shipped.Count, seed.Values.GetLength(0));
        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        Assert.All(shipped, p => Assert.Contains(p.Id.ToString(), sql));
        var indexes = ops.OfType<CreateIndexOperation>().ToList();
        Assert.Contains(indexes, i => i.IsUnique && i.Table == "permissions" && i.Columns.SequenceEqual(["code"]));
        Assert.Contains(indexes, i => i.IsUnique && i.Table == "role_permissions" && i.Columns.SequenceEqual(["role_id", "permission_id"]));
        Assert.Contains(indexes, i => i.IsUnique && i.Table == "store_member_permissions" && i.Columns.SequenceEqual(["store_member_id", "permission_id"]));
    }
}
