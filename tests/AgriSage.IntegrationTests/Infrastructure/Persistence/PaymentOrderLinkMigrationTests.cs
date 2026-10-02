using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Reviews PaymentOrderLinkAndOrderRefunds without a database (decisions C-D1 / C-D2, database design §35.18):
// payments.order_id and cancelled-order refunds, nothing else.
public class PaymentOrderLinkMigrationTests
{
    private static IReadOnlyList<MigrationOperation> Up => new PaymentOrderLinkAndOrderRefunds().UpOperations;

    [Fact]
    public void Up_only_adds_columns_relaxes_one_column_and_adds_indexes_checks_and_foreign_keys()
    {
        Assert.DoesNotContain(Up, operation =>
            operation is CreateTableOperation or DropTableOperation or DropColumnOperation or DropIndexOperation
                or DropCheckConstraintOperation or RenameTableOperation or RenameColumnOperation or SqlOperation
                or DeleteDataOperation or UpdateDataOperation or InsertDataOperation);

        Assert.Equal(
            ["payments.order_id", "refunds.order_id"],
            Up.OfType<AddColumnOperation>().Select(column => $"{column.Table}.{column.Name}").Order());
        Assert.All(Up.OfType<AddColumnOperation>(), column => Assert.True(column.IsNullable));

        var relaxed = Assert.Single(Up.OfType<AlterColumnOperation>());
        Assert.Equal("refunds.sales_return_id", $"{relaxed.Table}.{relaxed.Name}");
        Assert.True(relaxed.IsNullable);
        Assert.False(relaxed.OldColumn.IsNullable);
    }

    [Fact]
    public void Checks_bind_the_order_to_the_payment_context_and_give_each_refund_exactly_one_source()
    {
        var checks = Up.OfType<AddCheckConstraintOperation>().ToDictionary(check => check.Name, check => check.Sql);

        Assert.Equal(
            "(payment_context = 'ORDER_PAYMENT' AND order_id IS NOT NULL) OR (payment_context = 'DEBT_REPAYMENT' AND order_id IS NULL)",
            checks["ck_payments_order_context"]);
        Assert.Equal("num_nonnulls(sales_return_id, order_id) = 1", checks["ck_refunds_source"]);
        Assert.Equal("order_id IS NULL OR original_payment_id IS NOT NULL", checks["ck_refunds_order_payment"]);
        Assert.Equal(3, checks.Count);
    }

    [Fact]
    public void New_foreign_keys_are_indexed_and_never_cascade()
    {
        var foreignKeys = Up.OfType<AddForeignKeyOperation>().ToList();

        Assert.Equal(
            ["fk_payments_order_id", "fk_refunds_order_id", "fk_refunds_sales_return_id"],
            foreignKeys.Select(foreignKey => foreignKey.Name).Order());
        Assert.All(foreignKeys, foreignKey => Assert.Contains(foreignKey.OnDelete, new[] { ReferentialAction.Restrict, ReferentialAction.NoAction }));
        Assert.Equal(
            ["ix_payments_order_id", "ix_refunds_order_id"],
            Up.OfType<CreateIndexOperation>().Select(index => index.Name).Order());
    }

    [Fact]
    public void Down_restores_the_previous_schema_without_a_default_value()
    {
        var down = new PaymentOrderLinkAndOrderRefunds().DownOperations;

        Assert.Equal(
            ["payments.order_id", "refunds.order_id"],
            down.OfType<DropColumnOperation>().Select(column => $"{column.Table}.{column.Name}").Order());
        var restored = Assert.Single(down.OfType<AlterColumnOperation>());
        Assert.False(restored.IsNullable);
        Assert.Null(restored.DefaultValue);
    }
}
