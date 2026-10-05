using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class CreditDebtPostingSafetyMigrationTests
{
    [Fact]
    public void Up_only_extends_payment_methods_and_adds_unique_posting_sources()
    {
        var ops = new CreditDebtPostingSafety().UpOperations;
        Assert.Equal(4, ops.Count);
        var removed = Assert.Single(ops.OfType<DropCheckConstraintOperation>());
        Assert.Equal("ck_payments_payment_method", removed.Name);
        var check = Assert.Single(ops.OfType<AddCheckConstraintOperation>());
        Assert.Equal("payment_method IN ('CASH', 'PAYOS', 'BANK_TRANSFER')", check.Sql);
        var indexes = ops.OfType<CreateIndexOperation>().ToList();
        Assert.Equal(2, indexes.Count);
        Assert.All(indexes, i => { Assert.True(i.IsUnique); Assert.NotNull(i.Filter); Assert.Single(i.Columns); });
        Assert.Contains(indexes, i => i.Table == "debt_entries" && i.Columns[0] == "source_stock_movement_id");
        Assert.Contains(indexes, i => i.Table == "debt_transactions" && i.Columns[0] == "payment_allocation_id");
        Assert.DoesNotContain(ops, op => op is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation);
    }
    [Fact]
    public void Down_preserves_tables_and_does_not_delete_bank_receipts()
    {
        var ops = new CreditDebtPostingSafety().DownOperations;
        Assert.Equal(2, ops.OfType<DropIndexOperation>().Count());
        Assert.Equal("payment_method IN ('CASH', 'PAYOS')", Assert.Single(ops.OfType<AddCheckConstraintOperation>()).Sql);
        // PostgreSQL will refuse the old check if bank receipts exist: rollback never silently discards them.
        Assert.DoesNotContain(ops, op => op is DropTableOperation or DropColumnOperation or DeleteDataOperation or UpdateDataOperation);
    }
}
