using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class InventoryReplenishmentMigrationTests
{
    public static readonly string[] NewCodes = ["REPLENISHMENT.READ", "SEASONAL_RULES.READ", "SEASONAL_RULES.MANAGE"];
    [Fact]
    public void Up_only_adds_planning_tables_settings_permissions_and_realtime_hooks()
    {
        var ops = new InventoryReplenishment().UpOperations;
        Assert.Equal(new[] { "inventory_seasonal_rule_items", "inventory_seasonal_rules" },
            ops.OfType<CreateTableOperation>().Select(o => o.Name).Order());
        Assert.Equal(5, ops.OfType<AddColumnOperation>().Count());
        Assert.All(ops.OfType<AddColumnOperation>(), o => Assert.Equal("store_products", o.Table));
        Assert.DoesNotContain(ops, o => o is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation);
        Assert.All(ops.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys),
            fk => Assert.Contains(fk.OnDelete, new[] { ReferentialAction.NoAction, ReferentialAction.Restrict }));
        var sql = string.Join("\n", ops.OfType<SqlOperation>().Select(o => o.Sql));
        Assert.Contains("ON CONFLICT (role_id, permission_id) DO NOTHING", sql);
        Assert.Contains("agrisage_capture_planning_change", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.DoesNotContain("stock_movements", sql);
    }
}
