using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// F3.20: store_debt_policies.reminders_start_date (T0 of debt reminders). One nullable column; nothing is changed or removed.
public class DebtReminderStartDateMigrationTests
{
    private const string Previous = "20261102205426_CropSeasonDebtPermissions";
    private const string Current = "20261103161207_DebtReminderStartDate";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Up_only_adds_one_nullable_date_column_and_down_only_drops_it()
    {
        var migration = new DebtReminderStartDate();
        var add = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal(("store_debt_policies", "reminders_start_date", "date", true), (add.Table, add.Name, add.ColumnType, add.IsNullable));
        Assert.Null(add.DefaultValue);

        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(migration.DownOperations));
        Assert.Equal(("store_debt_policies", "reminders_start_date"), (drop.Table, drop.Name));
    }

    // Real PostgreSQL: the column appears nullable (so every store starts "not started": the job sends nothing) and goes away on down.
    [CreditDbFact]
    public async Task Migration_adds_and_removes_the_column_on_real_postgres()
    {
        var admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        var schema = "f320_migration_" + Guid.NewGuid().ToString("N");
        await Exec(admin, $"CREATE SCHEMA \"{schema}\"");
        var connection = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
        AgriSageDbContext Context() => new(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection).Options);
        try
        {
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            Assert.Null(await ColumnNullable(connection, schema));

            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Current, Token);
            Assert.Equal("YES", await ColumnNullable(connection, schema));

            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            Assert.Null(await ColumnNullable(connection, schema));
        }
        finally
        {
            await Exec(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    // Existing policy data survives both directions: a store with a saved policy (changed values) keeps every value, starts with
    // reminders not started (null), and the date set afterwards disappears only with the column on the way down.
    [CreditDbFact]
    public async Task Existing_policy_rows_keep_their_values_up_and_down_on_real_postgres()
    {
        var admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        var schema = "f320_data_" + Guid.NewGuid().ToString("N");
        await Exec(admin, $"CREATE SCHEMA \"{schema}\"");
        var connection = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
        AgriSageDbContext Context() => new(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection).Options);
        const string snapshot = "SELECT concat_ws('|', default_grace_days, reminder_offsets::text, staff_rollover_limit, owner_review_after_rollovers, " +
            "on_time_seasons_for_limit_raise, coalesce(max_credit_term_days::text, '-'), max_total_extension_days, version, deleted_at IS NULL) " +
            "FROM store_debt_policies";
        try
        {
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            await Exec(connection, """
                INSERT INTO stores (id, code, name, address_line, province, status, created_at, updated_at)
                VALUES ('11111111-1111-1111-1111-111111111111', 'S1', 'Store', '1 Street', 'Province', 'ACTIVE', now(), now());
                INSERT INTO store_debt_policies (id, store_id, default_grace_days, reminder_offsets, staff_rollover_limit,
                    owner_review_after_rollovers, on_time_seasons_for_limit_raise, max_credit_term_days, max_total_extension_days,
                    version, created_at, updated_at)
                VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 45,
                    '{"n2":-5,"n4":10,"n5":20}', 2, 3, 4, 90, 180, 7, now(), now());
                """);
            var before = await Scalar(connection, snapshot);
            Assert.StartsWith("45|", before);

            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Current, Token);
            Assert.Equal(before, await Scalar(connection, snapshot));
            Assert.Null(await Scalar(connection, "SELECT reminders_start_date::text FROM store_debt_policies"));   // not started

            await Exec(connection, "UPDATE store_debt_policies SET reminders_start_date = DATE '2026-11-03'");
            Assert.Equal("2026-11-03", await Scalar(connection, "SELECT reminders_start_date::text FROM store_debt_policies"));

            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            Assert.Equal(before, await Scalar(connection, snapshot));
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Current, Token);
            Assert.Equal(before, await Scalar(connection, snapshot));
            Assert.Null(await Scalar(connection, "SELECT reminders_start_date::text FROM store_debt_policies"));   // the date went with the column
        }
        finally
        {
            await Exec(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    private static async Task<string?> Scalar(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, c);
        var value = await command.ExecuteScalarAsync(CancellationToken.None);
        return value is null or DBNull ? null : (string)value;
    }

    private static async Task<string?> ColumnNullable(string connectionString, string schema)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = @s AND table_name = 'store_debt_policies' AND column_name = 'reminders_start_date'", c);
        command.Parameters.AddWithValue("s", schema);
        return (string?)await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
