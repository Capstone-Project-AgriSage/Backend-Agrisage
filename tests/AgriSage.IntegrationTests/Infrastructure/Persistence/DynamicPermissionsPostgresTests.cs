using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;
public sealed class DynamicPermissionsPostgresTests
{
    [RealDbFact]
    public async Task Reviewed_migration_sql_executes_on_postgresql_and_rolls_back()
    {
        await using var connection = new NpgsqlConnection(RealDb.ConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var context = new AgriSageDbContext(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection).Options);
        // A separate schema isolates the migration review even after the real dev migration has been applied.
        await using var setup = new NpgsqlCommand("CREATE SCHEMA permission_migration_review; SET LOCAL search_path TO permission_migration_review;", connection, transaction);
        await setup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var migrator = context.GetService<IMigrator>();
        var script = migrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.NoTransactions);
        await using var command = new NpgsqlCommand(script, connection, transaction) { CommandTimeout = 90 };
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM permissions", connection, transaction);
        Assert.Equal(190L, await count.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using var seed = new NpgsqlCommand("INSERT INTO roles(id,code,name,is_active,created_at,updated_at) VALUES(gen_random_uuid(),'STORE_OWNER','Owner',true,now(),now())", connection, transaction);
        await seed.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var defaultsSql = new AgriSage.Infrastructure.Persistence.Migrations.DynamicPermissions().UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Single().Sql;
        await using var defaults = new NpgsqlCommand(defaultsSql, connection, transaction);
        await defaults.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await using var granted = new NpgsqlCommand("SELECT count(*) FROM role_permissions", connection, transaction);
        Assert.True((long)(await granted.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
    }
}
