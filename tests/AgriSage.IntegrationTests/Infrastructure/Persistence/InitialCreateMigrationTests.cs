using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Reviews InitialCreate without a database: migrations are the schema source of truth after AGRI-14.
public class InitialCreateMigrationTests
{
    private static AgriSageDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AgriSageDbContext>()
            .UseNpgsql("Host=localhost;Database=agrisage_model_only")
            .Options);

    // Every new migration is added here on purpose, so it cannot slip in without a reviewed test.
    [Fact]
    public void Migrations_are_the_reviewed_list_in_order()
    {
        using var context = CreateContext();

        Assert.Equal(
            [
                "20260929092546_InitialCreate",
                "20261002155111_PaymentOrderLinkAndOrderRefunds",
                "20261003001732_StocktakeItemSnapshotTime",
                "20261003085951_CustomerGroupDefaultCreditTier",
                "20261004055235_CreditDebtPostingSafety",
                "20261007062351_AuthSessionsNotificationsOperations",
                "20261009151702_DynamicPermissions"
            ],
            context.Database.GetMigrations());
    }

    [Fact]
    public void Model_snapshot_matches_the_current_model()
    {
        using var context = CreateContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Up_creates_the_67_tables_without_destructive_operations()
    {
        var operations = new InitialCreate().UpOperations;

        Assert.Equal(67, operations.OfType<CreateTableOperation>().Count());
        Assert.DoesNotContain(operations, operation =>
            operation is DropTableOperation or DropColumnOperation or DropIndexOperation or DropForeignKeyOperation
                or RenameTableOperation or RenameColumnOperation or AlterColumnOperation or AlterTableOperation
                or DeleteDataOperation or UpdateDataOperation or InsertDataOperation);
    }

    [Fact]
    public void No_foreign_key_cascades_or_sets_null()
    {
        var operations = new InitialCreate().UpOperations;
        var foreignKeys = operations.OfType<CreateTableOperation>().SelectMany(table => table.ForeignKeys)
            .Concat(operations.OfType<AddForeignKeyOperation>())
            .ToList();

        Assert.Equal(263, foreignKeys.Count);
        Assert.All(foreignKeys, foreignKey => Assert.Contains(foreignKey.OnDelete, new[] { ReferentialAction.Restrict, ReferentialAction.NoAction }));
    }

    [Fact]
    public void Expression_indexes_are_created_last_and_dropped_first_from_PostgreSqlRawIndexes()
    {
        var migration = new InitialCreate();

        var upSql = migration.UpOperations.OfType<SqlOperation>().Select(operation => operation.Sql);
        var downSql = migration.DownOperations.Take(2).OfType<SqlOperation>().Select(operation => operation.Sql);

        Assert.Equal(PostgreSqlRawIndexes.All, upSql);
        Assert.IsType<SqlOperation>(migration.UpOperations[^1]);
        Assert.Equal([PostgreSqlRawIndexes.DropUsersEmailLower, PostgreSqlRawIndexes.DropInventoryLotsLogicalLot], downSql);
    }

    [Fact]
    public void Down_drops_every_table_it_created()
    {
        var migration = new InitialCreate();

        var created = migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order();
        var dropped = migration.DownOperations.OfType<DropTableOperation>().Select(table => table.Name).Order();

        Assert.Equal(created, dropped);
    }
}
