using AgriSage.Application.Features.Permissions;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class ExchangeMigrationTests
{
    [Fact]
    public void Exchange_extension_only_adds_tables_permissions_and_expands_payment_methods()
    {
        var migration = new SalesExchanges(); var ops = migration.UpOperations;
        Assert.Equal(new[] { "exchange_items", "sales_exchanges" }, ops.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.DoesNotContain(ops, o => o is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.All(ops.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk => Assert.Contains(fk.OnDelete, new[] { ReferentialAction.Restrict, ReferentialAction.NoAction }));
        Assert.Contains(ops.OfType<CreateIndexOperation>(), i => i.IsUnique && i.Columns.SequenceEqual(new[] { "store_id", "requested_by", "request_key" }));
        Assert.Contains(ops.OfType<CreateIndexOperation>(), i => i.IsUnique && i.Columns.SequenceEqual(new[] { "sales_return_id" }));
        Assert.Contains(ops.OfType<CreateIndexOperation>(), i => i.IsUnique && i.Columns.SequenceEqual(new[] { "replacement_order_id" }));
        var seed = Assert.Single(ops.OfType<InsertDataOperation>()); Assert.Equal(12, seed.Values.GetLength(0));
        var sql = Assert.Single(ops.OfType<SqlOperation>()).Sql;
        foreach (var definition in PermissionCatalog.Entries.Where(e => e.Module is "EXCHANGES" or "MY_EXCHANGES"))
            Assert.Contains(definition.Id.ToString(), sql);
        Assert.All(ops.OfType<AddCheckConstraintOperation>(), c => Assert.Contains("EXCHANGE_CREDIT", c.Sql));
        var downgrade = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("Cannot downgrade", downgrade); Assert.Contains("SELECT 1 FROM sales_exchanges", downgrade);
    }
}
