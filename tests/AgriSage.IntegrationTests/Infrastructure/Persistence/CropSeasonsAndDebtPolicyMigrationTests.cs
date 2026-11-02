using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// F3.9: crop_seasons, store_debt_policies and the season / rollover / reason-code columns of debt and orders.
public class CropSeasonsAndDebtPolicyMigrationTests
{
    private const string Previous = "20261102193332_LockDebtPermissionDelegation";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Up_is_additive_backfills_the_original_due_date_and_keeps_every_fk_restrictive()
    {
        var up = new CropSeasonsAndDebtPolicy().UpOperations;
        Assert.Equal(new[] { "crop_seasons", "store_debt_policies" }, up.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.DoesNotContain(up, o => o is DropTableOperation or DropColumnOperation or DeleteDataOperation or RenameColumnOperation);
        Assert.All(up.OfType<AddColumnOperation>(), c => Assert.True(c.IsNullable || c.DefaultValue is not null || c.Name == "rollover_count", c.Name));
        var original = Assert.Single(up.OfType<AddColumnOperation>(), c => c.Name == "original_due_date");
        Assert.True(original.IsNullable);
        Assert.Null(original.DefaultValue);
        var alter = Assert.Single(up.OfType<AlterColumnOperation>());
        Assert.Equal(("debt_entries", "original_due_date", false), (alter.Table, alter.Name, alter.IsNullable));
        var sql = string.Join("\n", up.OfType<SqlOperation>().Select(o => o.Sql));
        Assert.Contains("SET original_due_date = due_date", sql);
        // The backfill runs between the nullable add and the NOT NULL change.
        var ops = up.ToList();
        Assert.True(ops.IndexOf(original) < ops.FindIndex(o => o is SqlOperation) && ops.FindIndex(o => o is SqlOperation) < ops.IndexOf(alter));
        Assert.All(up.OfType<AddForeignKeyOperation>(), fk => Assert.Contains(fk.OnDelete, new[] { ReferentialAction.NoAction, ReferentialAction.Restrict }));
        Assert.All(up.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys),
            fk => Assert.Contains(fk.OnDelete, new[] { ReferentialAction.NoAction, ReferentialAction.Restrict }));
        Assert.Contains(up.OfType<AddCheckConstraintOperation>(), c => c.Name == "ck_debt_entry_actions_action_type" && c.Sql.Contains("'ROLLOVER'"));
        Assert.Contains(up.OfType<AddCheckConstraintOperation>(), c => c.Name == "ck_debt_entry_actions_reason_code" && c.Sql.Contains("'KHONG_THU_HOI'"));

        var down = new CropSeasonsAndDebtPolicy().DownOperations;
        Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("RAISE EXCEPTION", ((SqlOperation)down[0]).Sql);
    }

    // Real PostgreSQL: existing debt gets its current due date as original due date; the rollback refuses to erase season
    // data and runs cleanly once there is none.
    [CreditDbFact]
    public async Task Migration_backfills_existing_debt_and_refuses_to_roll_back_over_season_data()
    {
        var admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        var schema = "f39_migration_" + Guid.NewGuid().ToString("N");
        await Exec(admin, $"CREATE SCHEMA \"{schema}\"");
        var connection = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
        AgriSageDbContext Context() => new(new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql(connection).Options);
        try
        {
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            await Exec(connection, """
                INSERT INTO roles (id, code, name, created_at, updated_at) VALUES ('00000000-0000-0000-0000-00000000f001', 'FARMER', 'Farmer', now(), now());
                INSERT INTO users (id, role_id, password_hash, full_name, email, status, created_at, updated_at)
                  VALUES ('00000000-0000-0000-0000-00000000f002', '00000000-0000-0000-0000-00000000f001', 'x', 'Bảy', 'bay@example.test', 'ACTIVE', now(), now());
                INSERT INTO farmer_profiles (id, user_id, created_at, updated_at) VALUES ('00000000-0000-0000-0000-00000000f003', '00000000-0000-0000-0000-00000000f002', now(), now());
                INSERT INTO stores (id, code, name, address_line, province, status, created_at, updated_at)
                  VALUES ('00000000-0000-0000-0000-00000000f004', 'F39', 'Store', 'Street', 'Province', 'ACTIVE', now(), now());
                INSERT INTO debt_accounts (id, store_id, farmer_profile_id, status, created_at, updated_at)
                  VALUES ('00000000-0000-0000-0000-00000000f005', '00000000-0000-0000-0000-00000000f004', '00000000-0000-0000-0000-00000000f003', 'ACTIVE', now(), now());
                INSERT INTO debt_entries (id, debt_account_id, entry_number, source_type, fulfillment_value, original_amount, outstanding_amount, due_date, status, created_by, created_at, updated_at)
                  VALUES ('00000000-0000-0000-0000-00000000f006', '00000000-0000-0000-0000-00000000f005', 'DE-1', 'MANUAL_ADJUSTMENT', 1200000, 1200000, 1200000, '2027-04-14', 'OPEN', '00000000-0000-0000-0000-00000000f002', now(), now()),
                         ('00000000-0000-0000-0000-00000000f007', '00000000-0000-0000-0000-00000000f005', 'DE-2', 'MANUAL_ADJUSTMENT', 800000, 800000, 0, '2026-12-01', 'PAID', '00000000-0000-0000-0000-00000000f002', now(), now());
                """);

            // Up to this migration only, so later migrations cannot change what this test checks.
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync("20261102203458_CropSeasonsAndDebtPolicy", Token);
            await using (var db = Context())
            {
                var entries = await db.DebtEntries.AsNoTracking().OrderBy(e => e.EntryNumber).ToListAsync(Token);
                Assert.Equal(new[] { new DateOnly(2027, 4, 14), new DateOnly(2026, 12, 1) }, entries.Select(e => e.OriginalDueDate));
                Assert.All(entries, e => Assert.Equal((0, (Guid?)null, (Guid?)null, (string?)null),
                    (e.RolloverCount, e.OriginCropSeasonId, e.CollectionCropSeasonId, e.DueBasis)));
            }

            await Exec(connection, """
                INSERT INTO crop_seasons (id, store_id, code, name, order_window_start, harvest_start, harvest_end, grace_days, payment_due_date, created_at, updated_at)
                  VALUES ('00000000-0000-0000-0000-00000000f008', '00000000-0000-0000-0000-00000000f004', 'DX-2026', 'Đông Xuân', '2026-10-15', '2027-02-15', '2027-03-15', 30, '2027-04-14', now(), now());
                """);
            await using (var db = Context())
            {
                var refused = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(Previous, Token));
                Assert.Contains("Cannot roll back CropSeasonsAndDebtPolicy", (refused as PostgresException ?? refused.InnerException)?.Message ?? refused.Message);
            }
            await using (var db = Context()) Assert.Contains("20261102203458_CropSeasonsAndDebtPolicy", await db.Database.GetAppliedMigrationsAsync(Token));

            await Exec(connection, "DELETE FROM crop_seasons");
            await using (var db = Context()) await db.GetService<IMigrator>().MigrateAsync(Previous, Token);
            await using (var db = Context())
            {
                Assert.DoesNotContain("20261102203458_CropSeasonsAndDebtPolicy", await db.Database.GetAppliedMigrationsAsync(Token));
                Assert.Equal(2, await Scalar(connection, "SELECT count(*) FROM debt_entries"));
                Assert.Equal(0, await Scalar(connection, $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name = 'crop_seasons'"));
            }
        }
        finally
        {
            await Exec(admin, $"DROP SCHEMA \"{schema}\" CASCADE");
        }
    }

    // The database itself refuses what the domain also refuses, so no other writer can bypass the rules.
    [CreditDbFact]
    public async Task Database_constraints_enforce_season_dates_reason_codes_credit_only_seasons_and_restrictive_fks()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.FulfillAsync(5, Token);
        var season = new CropSeason(e.StoreId, "DX-2026", "Đông Xuân", "RICE", new DateOnly(2026, 10, 15), new DateOnly(2027, 2, 15), new DateOnly(2027, 3, 15), 30);
        Guid entryId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            db.CropSeasons.Add(season);
            await db.SaveChangesAsync(Token);
            entryId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE debt_entries SET origin_crop_season_id = {season.Id}, collection_crop_season_id = {season.Id} WHERE id = {entryId}", Token);
        }

        async Task Rejected(string sql, string constraint)
        {
            using var s = e.Scope();
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var thrown = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
            var error = thrown as PostgresException ?? thrown.InnerException as PostgresException
                ?? throw new Xunit.Sdk.XunitException($"Expected a PostgreSQL violation of {constraint}, got: {thrown}");
            Assert.Equal(constraint, error.ConstraintName);
        }

        await Rejected($"UPDATE crop_seasons SET payment_due_date = '2027-04-15' WHERE id = '{season.Id}'", "ck_crop_seasons_payment_due_date");
        await Rejected($"UPDATE crop_seasons SET harvest_end = harvest_start WHERE id = '{season.Id}'", "ck_crop_seasons_harvest_dates");
        await Rejected($"UPDATE crop_seasons SET order_window_start = harvest_start WHERE id = '{season.Id}'", "ck_crop_seasons_order_window");
        await Rejected($"UPDATE crop_seasons SET closed_at = now() WHERE id = '{season.Id}'", "ck_crop_seasons_closed");
        await Rejected($"""
            INSERT INTO crop_seasons (id, store_id, code, name, order_window_start, harvest_start, harvest_end, grace_days, payment_due_date, created_at, updated_at)
            VALUES (gen_random_uuid(), '{e.StoreId}', 'DX-2026', 'Copy', '2027-02-15', '2027-07-15', '2027-08-15', 30, '2027-09-14', now(), now())
            """, "ux_crop_seasons_store_id_code");
        await Rejected($"UPDATE orders SET crop_season_id = '{season.Id}', settlement_type = 'FULL_PAYMENT' WHERE id = '{order.Id}'", "ck_orders_crop_season_credit_only");
        await Rejected($"UPDATE debt_entries SET rollover_count = -1 WHERE id = '{entryId}'", "ck_debt_entries_rollover_count");
        await Rejected($"""
            INSERT INTO debt_entry_actions (id, debt_entry_id, action_type, reason, reason_code, created_by, created_at, updated_at)
            VALUES (gen_random_uuid(), '{entryId}', 'ADJUST', 'x', 'BOGUS', '{e.User.UserId}', now(), now())
            """, "ck_debt_entry_actions_reason_code");
        await Rejected($"DELETE FROM crop_seasons WHERE id = '{season.Id}'", "fk_debt_entries_collection_crop_season_id");
        await Rejected($"INSERT INTO store_debt_policies (id, store_id, reminder_offsets, staff_rollover_limit, owner_review_after_rollovers, created_at, updated_at) VALUES (gen_random_uuid(), '{e.StoreId}', '{{{{}}}}', 3, 2, now(), now())",
            "ck_store_debt_policies_owner_review_after_rollovers");

        // A legacy action without a code and a ROLLOVER action are accepted.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var actor = e.User.UserId!.Value;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO debt_entry_actions (id, debt_entry_id, action_type, reason, reason_code, old_crop_season_id, new_crop_season_id, created_by, created_at, updated_at)
                VALUES (gen_random_uuid(), {entryId}, 'KEEP', 'legacy', NULL, NULL, NULL, {actor}, now(), now()),
                       (gen_random_uuid(), {entryId}, 'ROLLOVER', 'Mất mùa', 'MAT_MUA', {season.Id}, {season.Id}, {actor}, now(), now())
                """, Token);
        }

        // The policy row round-trips through EF with its jsonb reminder offsets.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            db.StoreDebtPolicies.Add(new StoreDebtPolicy(e.StoreId));
            await db.SaveChangesAsync(Token);
        }
        using (var s = e.Scope())
        {
            var policy = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().StoreDebtPolicies.AsNoTracking().SingleAsync(Token);
            Assert.Equal(DebtReminderOffsets.Default, policy.ReminderOffsets);
            Assert.Equal((30, 365, (int?)null), (policy.DefaultGraceDays, policy.MaxTotalExtensionDays, policy.MaxCreditTermDays));
        }
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> Scalar(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(sql, c);
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }
}
